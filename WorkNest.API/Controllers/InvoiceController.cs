using WorkNest.Application.DTOs.Booking;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Payment;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class InvoiceController : ControllerBase
    {
        private readonly IPaymentService _payments;
        private readonly IBookingService _bookings;
        private readonly IDbRepository _db;
        private readonly IPdfService _pdf;
        private readonly IConfiguration _config;

        public InvoiceController(IPaymentService payments, IBookingService bookings, IDbRepository db, IPdfService pdf, IConfiguration config)
        {
            _payments = payments;
            _bookings = bookings;
            _db = db;
            _pdf = pdf;
            _config = config;
        }

        private string GetConnectionString()
        {
            return _config.GetConnectionString("DefaultConnection") 
                ?? _config["ConnectionStrings:DefaultConnection"] 
                ?? "";
        }

        private string? ResolveUserEmail(string? headerEmail)
        {
            var claimEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                             ?? User.FindFirst("email")?.Value;
            return !string.IsNullOrWhiteSpace(claimEmail) ? claimEmail : headerEmail;
        }

        [HttpGet("api/invoice")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null,
            [FromQuery] int? typeId = null)
        {
            try
            {
                if (page <= 0) page = 1;
                if (limit <= 0) limit = 10;
                int offset = (page - 1) * limit;

                using var conn = new SqlConnection(GetConnectionString());
                await conn.OpenAsync();

                string countSql = @"
                    SELECT COUNT(*)
                    FROM dbo.WN_Invoices i WITH (NOLOCK)
                    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
                    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = i.UserId
                    WHERE (@Search IS NULL OR @Search = '' OR i.InvoiceNumber LIKE '%' + @Search + '%' OR u.Email LIKE '%' + @Search + '%' OR c.FirstName LIKE '%' + @Search + '%');";

                using var countCmd = new SqlCommand(countSql, conn);
                countCmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
                int total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

                string dataSql = @"
                    SELECT 
                        i.Id,
                        i.PublicId,
                        i.InvoiceNumber,
                        i.UserId,
                        i.BookingId,
                        i.IssuedOn,
                        i.DueOn,
                        ISNULL(i.SubTotal, 0) AS SubTotal,
                        ISNULL(i.DiscountTotal, 0) AS DiscountTotal,
                        ISNULL(i.TaxTotal, 0) AS TaxTotal,
                        ISNULL(i.GrandTotal, 0) AS GrandTotal,
                        ISNULL(i.PaidTotal, 0) AS PaidTotal,
                        (ISNULL(i.GrandTotal, 0) - ISNULL(i.PaidTotal, 0)) AS BalanceDue,
                        i.CurrencyCode,
                        i.StatusId,
                        CASE 
                            WHEN i.StatusId = 2 THEN 'Paid'
                            WHEN i.StatusId = 3 THEN 'Partial'
                            WHEN i.StatusId = 4 THEN 'Overdue'
                            ELSE 'Unpaid'
                        END AS StatusLabel,
                        i.Notes,
                        i.CreatedOn,
                        ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), ISNULL(u.Name, u.Email)) AS CustomerName,
                        u.Email AS UserEmail,
                        s.Name AS SpaceName,
                        s.Code AS SpaceCode,
                        b.StartOn,
                        b.EndOn
                    FROM dbo.WN_Invoices i WITH (NOLOCK)
                    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
                    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = i.UserId
                    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
                    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                    WHERE (@Search IS NULL OR @Search = '' OR i.InvoiceNumber LIKE '%' + @Search + '%' OR u.Email LIKE '%' + @Search + '%' OR c.FirstName LIKE '%' + @Search + '%')
                    ORDER BY i.Id DESC
                    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

                using var dataCmd = new SqlCommand(dataSql, conn);
                dataCmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
                dataCmd.Parameters.AddWithValue("@Offset", offset);
                dataCmd.Parameters.AddWithValue("@Limit", limit);

                var list = new List<Dictionary<string, object?>>();
                using var reader = await dataCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    list.Add(row);
                }

                return Ok(new
                {
                    isSuccessful = true,
                    data = list,
                    total = total,
                    page = page,
                    limit = limit
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        
        [HttpGet("api/invoice/{id:int}/pdf")]
        [AllowAnonymous]
        public async Task<IActionResult> GetInvoicePdf(int id)
        {
            int bookingId = 0;
            string invoiceNumber = id.ToString();
            using (var conn = new SqlConnection(GetConnectionString()))
            {
                await conn.OpenAsync();
                using var cmd = new SqlCommand("SELECT BookingId, InvoiceNumber FROM dbo.WN_Invoices WHERE Id = @Id;", conn);
                cmd.Parameters.AddWithValue("@Id", id);
                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    bookingId = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                    invoiceNumber = r.IsDBNull(1) ? id.ToString() : r.GetString(1);
                }
            }

            if (bookingId > 0)
            {
                var challanResult = await _bookings.GetChallanAsync(bookingId);
                if (challanResult.IsSuccessful && challanResult.Data is not null)
                {
                    var dto = (ChallanResponseDto)challanResult.Data;
                    var pdfBytes = _pdf.GenerateBookingConfirmationPdf(dto);
                    return File(pdfBytes, "application/pdf", $"Invoice-{invoiceNumber}.pdf");
                }
            }
            return NotFound(new { isSuccessful = false, message = "Invoice details could not be generated." });
        }

        [HttpGet("api/invoice/{id:int}")]
        public async Task<IActionResult> GetDetails(int id)
        {
            try
            {
                using var conn = new SqlConnection(GetConnectionString());
                await conn.OpenAsync();

                string invoiceSql = @"
                    SELECT 
                        i.*,
                        ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), ISNULL(u.Name, u.Email)) AS CustomerName,
                        u.Email AS UserEmail,
                        s.Name AS SpaceName,
                        s.Code AS SpaceCode,
                        b.StartOn,
                        b.EndOn
                    FROM dbo.WN_Invoices i WITH (NOLOCK)
                    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
                    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = i.UserId
                    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
                    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                    WHERE i.Id = @Id;";

                using var invCmd = new SqlCommand(invoiceSql, conn);
                invCmd.Parameters.AddWithValue("@Id", id);

                Dictionary<string, object?>? invoice = null;
                using (var reader = await invCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        invoice = new Dictionary<string, object?>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            invoice[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }
                    }
                }

                if (invoice == null)
                    return NotFound(new { isSuccessful = false, message = "Invoice not found" });

                string linesSql = "SELECT * FROM dbo.WN_InvoiceLines WHERE InvoiceId = @Id ORDER BY SortOrder, Id;";
                using var linesCmd = new SqlCommand(linesSql, conn);
                linesCmd.Parameters.AddWithValue("@Id", id);

                var lines = new List<Dictionary<string, object?>>();
                using (var reader = await linesCmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object?>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }
                        lines.Add(row);
                    }
                }

                invoice["lines"] = lines;
                return Ok(new { isSuccessful = true, data = invoice });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [HttpPost("api/invoice/{id:int}/record-payment")]
        public async Task<IActionResult> RecordPayment(
            int id,
            [FromBody] RecordPaymentDto req)
        {
            try
            {
                using var conn = new SqlConnection(GetConnectionString());
                await conn.OpenAsync();

                using var trans = conn.BeginTransaction();
                try
                {
                    // Check invoice
                    string checkSql = "SELECT GrandTotal, PaidTotal, UserId, BookingId FROM dbo.WN_Invoices WITH (UPDLOCK) WHERE Id = @Id;";
                    using var checkCmd = new SqlCommand(checkSql, conn, trans);
                    checkCmd.Parameters.AddWithValue("@Id", id);

                    decimal grandTotal = 0, paidTotal = 0;
                    int userId = 0, bookingId = 0;
                    using (var reader = await checkCmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            grandTotal = reader.GetDecimal(0);
                            paidTotal = reader.GetDecimal(1);
                            userId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                            bookingId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                        }
                        else
                        {
                            trans.Rollback();
                            return NotFound(new { isSuccessful = false, message = "Invoice not found" });
                        }
                    }

                    decimal newPaidTotal = paidTotal + req.PaidAmount;
                    byte newStatusId = 1; // Unpaid
                    if (newPaidTotal >= grandTotal) newStatusId = 2; // Paid
                    else if (newPaidTotal > 0) newStatusId = 3; // Partial

                    // Update Invoice
                    string updateSql = "UPDATE dbo.WN_Invoices SET PaidTotal = @PaidTotal, StatusId = @StatusId WHERE Id = @Id;";
                    using var updateCmd = new SqlCommand(updateSql, conn, trans);
                    updateCmd.Parameters.AddWithValue("@PaidTotal", newPaidTotal);
                    updateCmd.Parameters.AddWithValue("@StatusId", newStatusId);
                    updateCmd.Parameters.AddWithValue("@Id", id);
                    await updateCmd.ExecuteNonQueryAsync();

                    // Insert Payment record
                    string paySql = @"
                        INSERT INTO dbo.WN_Payments (
                            PublicId, Amount, PaymentMethod, PaymentStatus, PaidAt, CreatedAt,
                            BookingIdInt, InvoiceId, StatusId, Notes
                        )
                        VALUES (
                            NEWID(), @Amount, @Method, 'Completed', SYSUTCDATETIME(), SYSUTCDATETIME(),
                            @BookingId, @InvoiceId, 2, @Notes
                        );";
                    using var payCmd = new SqlCommand(paySql, conn, trans);
                    payCmd.Parameters.AddWithValue("@Amount", req.PaidAmount);
                    payCmd.Parameters.AddWithValue("@Method", req.PaymentMethod ?? "Cash");
                    payCmd.Parameters.AddWithValue("@BookingId", bookingId > 0 ? (object)bookingId : DBNull.Value);
                    payCmd.Parameters.AddWithValue("@InvoiceId", id);
                    payCmd.Parameters.AddWithValue("@Notes", req.Notes ?? "Invoice payment recorded");
                    await payCmd.ExecuteNonQueryAsync();

                    trans.Commit();
                    return Ok(new { isSuccessful = true, message = "Payment recorded successfully", paidTotal = newPaidTotal, statusId = newStatusId });
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [HttpPost("api/invoice/advance")]
        public async Task<IActionResult> CreateAdvance(
            [FromBody] AdvanceInvoiceRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            var email = ResolveUserEmail(userEmail);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity or email header required" });

            var result = await _payments.CreateAdvanceInvoiceAsync(request, email);
            if (!result.IsSuccessful) return BadRequest(result);
            return StatusCode(201, result);
        }
    }

    public class RecordPaymentDto
    {
        public decimal PaidAmount { get; set; }
        public string? PaymentMethod { get; set; }
        public string? TransactionRef { get; set; }
        public string? Notes { get; set; }
    }
}


