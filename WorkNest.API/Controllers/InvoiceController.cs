using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.DTOs.Payment;
using WorkNest.Application.Interfaces;
using WorkNest.Infrastructure.ExternalServices.Pdf;

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
        private readonly IEmailService _email;

        public InvoiceController(IPaymentService payments, IBookingService bookings, IDbRepository db, IPdfService pdf, IConfiguration config, IEmailService email)
        {
            _payments = payments;
            _bookings = bookings;
            _db = db;
            _pdf = pdf;
            _config = config;
            _email = email;
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

                using var cmd = new SqlCommand("dbo.WN_GetInvoicesList", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Page", page);
                cmd.Parameters.AddWithValue("@Limit", limit);
                cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TypeId", (object?)typeId ?? DBNull.Value);

                int totalCount = 0;
                var items = new List<Dictionary<string, object?>>();

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        totalCount = reader.GetInt32(0);
                    }

                    if (await reader.NextResultAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var row = new Dictionary<string, object?>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                            }
                            items.Add(row);
                        }
                    }
                }

                return Ok(new
                {
                    isSuccessful = true,
                    data = items,
                    totalCount = totalCount,
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
        [HttpGet("api/invoice/{id:int}/statement-pdf")]
        [AllowAnonymous]
        public async Task<IActionResult> GetStatementInvoicePdf(int id)
        {
            try
            {
                using var conn = new SqlConnection(GetConnectionString());
                await conn.OpenAsync();

                var dto = await BuildStatementInvoicePdfDtoAsync(id, conn);
                if (dto == null)
                    return NotFound(new { isSuccessful = false, message = "Invoice not found." });

                byte[] pdfBytes = StatementInvoicePdfGenerator.GeneratePdf(dto);
                return File(pdfBytes, "application/pdf", $"Statement-Invoice-{dto.InvoiceNumber}.pdf");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        private static bool HasColumn(SqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private async Task<StatementInvoicePdfDto?> BuildStatementInvoicePdfDtoAsync(int id, SqlConnection conn)
        {
            var dto = new StatementInvoicePdfDto();
            int userId = 0;

            using var cmd = new SqlCommand("dbo.WN_sp_GetStatementInvoicePdfData", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@InvoiceId", id);

            using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    userId = reader.IsDBNull(reader.GetOrdinal("UserId")) ? 0 : reader.GetInt32(reader.GetOrdinal("UserId"));
                    dto.InvoiceNumber = reader.GetString(reader.GetOrdinal("InvoiceNumber"));
                    dto.InvoiceDate = reader.GetDateTime(reader.GetOrdinal("IssuedOn"));
                    dto.DueDate = reader.GetDateTime(reader.GetOrdinal("DueOn"));
                    dto.BillingPeriodStart = HasColumn(reader, "BillingPeriodStart") && !reader.IsDBNull(reader.GetOrdinal("BillingPeriodStart")) ? reader.GetDateTime(reader.GetOrdinal("BillingPeriodStart")) : (DateTime?)null;
                    dto.BillingPeriodEnd = HasColumn(reader, "BillingPeriodEnd") && !reader.IsDBNull(reader.GetOrdinal("BillingPeriodEnd")) ? reader.GetDateTime(reader.GetOrdinal("BillingPeriodEnd")) : (DateTime?)null;
                    dto.CurrentInvoiceTotal = reader.GetDecimal(reader.GetOrdinal("GrandTotal"));
                    dto.CurrencyCode = reader.GetString(reader.GetOrdinal("CurrencyCode"));
                    dto.AccountName = reader.GetString(reader.GetOrdinal("AccountName"));
                    dto.AttnName = reader.IsDBNull(reader.GetOrdinal("AttnName")) ? "" : reader.GetString(reader.GetOrdinal("AttnName"));
                    dto.BillingAddress = reader.IsDBNull(reader.GetOrdinal("BillingAddress")) ? "" : reader.GetString(reader.GetOrdinal("BillingAddress"));
                    dto.AccountNumber = reader.GetString(reader.GetOrdinal("AccountNumber"));
                    dto.SntnNtnNic = reader.IsDBNull(reader.GetOrdinal("SntnNtnNic")) ? "" : reader.GetString(reader.GetOrdinal("SntnNtnNic"));
                    dto.CenterName = reader.GetString(reader.GetOrdinal("CenterName"));
                    dto.VendorLegalName = reader.GetString(reader.GetOrdinal("VendorLegalName"));
                    dto.VendorAddress = reader.IsDBNull(reader.GetOrdinal("VendorAddress")) ? "" : reader.GetString(reader.GetOrdinal("VendorAddress"));
                    dto.VendorPhone = reader.IsDBNull(reader.GetOrdinal("VendorPhone")) ? "" : reader.GetString(reader.GetOrdinal("VendorPhone"));
                    dto.VendorFax = reader.IsDBNull(reader.GetOrdinal("VendorFax")) ? "" : reader.GetString(reader.GetOrdinal("VendorFax"));
                    dto.VendorNtn = reader.IsDBNull(reader.GetOrdinal("VendorNtn")) ? "" : reader.GetString(reader.GetOrdinal("VendorNtn"));
                }
                else
                {
                    return null;
                }

                if (await reader.NextResultAsync())
                {
                    var items = new List<StatementInvoiceLineItemDto>();
                    while (await reader.ReadAsync())
                    {
                        int chargeTypeId = HasColumn(reader, "ChargeTypeId") && !reader.IsDBNull(reader.GetOrdinal("ChargeTypeId")) ? Convert.ToInt32(reader.GetValue(reader.GetOrdinal("ChargeTypeId"))) : 0;
                        string desc = reader.IsDBNull(reader.GetOrdinal("Description")) ? "Service Charge" : reader.GetString(reader.GetOrdinal("Description"));

                        bool isDeposit = chargeTypeId == 2
                            || desc.Contains("Security Deposit", StringComparison.OrdinalIgnoreCase)
                            || desc.Contains("Deposit", StringComparison.OrdinalIgnoreCase);

                        DateTime? lineFrom = null;
                        DateTime? lineTo = null;

                        if (isDeposit)
                        {
                            lineFrom = null;
                            lineTo = null;
                            if (!desc.Contains("(Refundable)", StringComparison.OrdinalIgnoreCase))
                            {
                                desc = desc.Trim() + " (Refundable)";
                            }
                        }
                        else
                        {
                            lineFrom = dto.BillingPeriodStart ?? dto.InvoiceDate;
                            lineTo = dto.BillingPeriodEnd ?? dto.DueDate;
                        }

                        items.Add(new StatementInvoiceLineItemDto
                        {
                            Description = desc,
                            FromDate = lineFrom,
                            ToDate = lineTo,
                            PriceExclVat = reader.GetDecimal(reader.GetOrdinal("PriceExclVat")),
                            VatAmount = reader.GetDecimal(reader.GetOrdinal("VatAmount")),
                            Category = reader.GetString(reader.GetOrdinal("CategoryName"))
                        });
                    }
                    if (items.Count > 0) dto.LineItems = items;
                }

                if (await reader.NextResultAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        dto.PreviousOutstandingBalance = reader.GetDecimal(reader.GetOrdinal("PriorBalance"));
                        dto.PaymentReceived = reader.GetDecimal(reader.GetOrdinal("PaymentReceived"));
                    }
                }

                if (await reader.NextResultAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        if (!reader.IsDBNull(0)) dto.BankName = reader.GetString(0);
                        if (!reader.IsDBNull(1)) dto.BankAccountNumber = reader.GetString(1);
                    }
                }
            }

            return dto;
        }

        [HttpGet("api/invoice/{id:int}")]
        public async Task<IActionResult> GetDetails(int id)
        {
            try
            {
                using var conn = new SqlConnection(GetConnectionString());
                await conn.OpenAsync();

                using var cmd = new SqlCommand("dbo.WN_sp_GetInvoiceDetailsById", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@InvoiceId", id);

                Dictionary<string, object?>? invoice = null;
                var lines = new List<Dictionary<string, object?>>();

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        invoice = new Dictionary<string, object?>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            invoice[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }
                    }

                    if (invoice == null)
                        return NotFound(new { isSuccessful = false, message = "Invoice not found" });

                    if (await reader.NextResultAsync())
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
                }

                invoice["lines"] = lines;
                return Ok(new { isSuccessful = true, data = invoice });
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

        [HttpPost("api/invoice")]
        public async Task<IActionResult> CreateCustomInvoice(
            [FromBody] CreateCustomInvoiceDto req,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            try
            {
                if (req == null || req.UserId <= 0 || req.Lines == null || req.Lines.Count == 0)
                    return BadRequest(new { isSuccessful = false, message = "UserId and at least one line item are required." });

                using var conn = new SqlConnection(GetConnectionString());
                await conn.OpenAsync();

                decimal subTotal = 0;
                decimal discountTotal = 0;
                decimal taxTotal = 0;

                foreach (var line in req.Lines)
                {
                    decimal lineNet = (line.Quantity * line.UnitPrice) - line.DiscountAmount;
                    decimal lineTax = Math.Round(lineNet * line.TaxRate, 4);
                    subTotal += (line.Quantity * line.UnitPrice);
                    discountTotal += line.DiscountAmount;
                    taxTotal += lineTax;
                }

                decimal grandTotal = subTotal - discountTotal + taxTotal;

                using var cmd = new SqlCommand("dbo.WN_sp_CreateCustomInvoice", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@UserId", req.UserId);
                cmd.Parameters.AddWithValue("@BookingId", (object?)req.BookingId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IssuedOn", req.IssuedOn);
                cmd.Parameters.AddWithValue("@DueOn", req.DueOn);
                cmd.Parameters.AddWithValue("@CurrencyCode", (object?)req.CurrencyCode ?? "PKR");
                cmd.Parameters.AddWithValue("@Notes", (object?)req.Notes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@SubTotal", subTotal);
                cmd.Parameters.AddWithValue("@DiscountTotal", discountTotal);
                cmd.Parameters.AddWithValue("@TaxTotal", taxTotal);
                cmd.Parameters.AddWithValue("@GrandTotal", grandTotal);

                var pInvoiceId = new SqlParameter("@InvoiceId", SqlDbType.Int) { Direction = ParameterDirection.Output };
                var pInvoiceNumber = new SqlParameter("@InvoiceNumber", SqlDbType.NVarChar, 50) { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(pInvoiceId);
                cmd.Parameters.Add(pInvoiceNumber);

                await cmd.ExecuteNonQueryAsync();

                int newInvoiceId = (int)pInvoiceId.Value;
                string invoiceNumber = (string)pInvoiceNumber.Value;

                short sortOrder = 1;
                foreach (var line in req.Lines)
                {
                    string lineSql = @"
                        INSERT INTO dbo.WN_InvoiceLines (
                            InvoiceId, ChargeTypeId, Description, Quantity,
                            UnitPrice, DiscountAmount, TaxRate, SortOrder
                        )
                        VALUES (
                            @InvoiceId, @ChargeTypeId, @Description, @Quantity,
                            @UnitPrice, @DiscountAmount, @TaxRate, @SortOrder
                        );";

                    using var lineCmd = new SqlCommand(lineSql, conn);
                    lineCmd.Parameters.AddWithValue("@InvoiceId", newInvoiceId);
                    lineCmd.Parameters.AddWithValue("@ChargeTypeId", line.ChargeTypeId > 0 ? line.ChargeTypeId : (byte)1);
                    lineCmd.Parameters.AddWithValue("@Description", string.IsNullOrWhiteSpace(line.Description) ? "Custom Charge" : line.Description);
                    lineCmd.Parameters.AddWithValue("@Quantity", line.Quantity > 0 ? line.Quantity : 1);
                    lineCmd.Parameters.AddWithValue("@UnitPrice", line.UnitPrice);
                    lineCmd.Parameters.AddWithValue("@DiscountAmount", line.DiscountAmount);
                    lineCmd.Parameters.AddWithValue("@TaxRate", line.TaxRate);
                    lineCmd.Parameters.AddWithValue("@SortOrder", sortOrder++);

                    await lineCmd.ExecuteNonQueryAsync();
                }

                string updateHeaderSql = @"
                    UPDATE dbo.WN_Invoices
                    SET BillingPeriodStart = @BillingPeriodStart,
                        BillingPeriodEnd = @BillingPeriodEnd,
                        AdvanceRentMonths = @AdvanceRentMonths,
                        SecurityDepositAmount = @SecurityDepositAmount,
                        SubTotal = @SubTotal
                    WHERE Id = @InvoiceId;";
                using var updateCmd = new SqlCommand(updateHeaderSql, conn);
                updateCmd.Parameters.AddWithValue("@InvoiceId", newInvoiceId);
                updateCmd.Parameters.AddWithValue("@BillingPeriodStart", (object?)req.BillingPeriodStart ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@BillingPeriodEnd", (object?)req.BillingPeriodEnd ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@AdvanceRentMonths", (object?)req.AdvanceRentMonths ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@SecurityDepositAmount", req.SecurityDepositAmount ?? 0m);
                updateCmd.Parameters.AddWithValue("@SubTotal", subTotal - discountTotal);
                await updateCmd.ExecuteNonQueryAsync();

                string emailNotice = "";
                if (req.SendEmail)
                {
                    string targetEmail = "";
                    string customerName = "";

                    using (var emailCmd = new SqlCommand("dbo.WN_GetInvoiceEmailData", conn))
                    {
                        emailCmd.CommandType = CommandType.StoredProcedure;
                        emailCmd.Parameters.AddWithValue("@InvoiceId", newInvoiceId);
                        using var r = await emailCmd.ExecuteReaderAsync();
                        if (await r.ReadAsync())
                        {
                            targetEmail = r.IsDBNull(r.GetOrdinal("TargetEmail")) ? "" : r.GetString(r.GetOrdinal("TargetEmail"));
                            customerName = r.IsDBNull(r.GetOrdinal("CustomerName")) ? "Valued Customer" : r.GetString(r.GetOrdinal("CustomerName"));
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(targetEmail))
                    {
                        byte[]? pdfBytes = null;
                        try
                        {
                            var pdfDto = await BuildStatementInvoicePdfDtoAsync(newInvoiceId, conn);
                            if (pdfDto != null)
                            {
                                pdfBytes = StatementInvoicePdfGenerator.GeneratePdf(pdfDto);
                            }
                        }
                        catch (Exception pdfEx)
                        {
                            Console.WriteLine($"[PDF Error] StatementInvoicePdfGenerator failed for custom invoice {newInvoiceId}: {pdfEx}");
                        }

                        try
                        {
                            await _email.SendChallanEmailAsync(
                                toEmail: targetEmail,
                                customerName: customerName,
                                challanNumber: invoiceNumber,
                                spaceName: "WorkNest Workspace / Custom Invoice",
                                billingPeriod: "Custom Billing",
                                totalPayable: grandTotal,
                                startOn: req.IssuedOn,
                                endOn: req.DueOn,
                                totalContractAmount: grandTotal,
                                currentCycleAmount: subTotal,
                                taxAmount: taxTotal,
                                discountAmount: discountTotal,
                                pdfBytes: pdfBytes
                            );
                            emailNotice = $" Email dispatched with 5-Page Statement PDF packet attached to {targetEmail}.";
                        }
                        catch (Exception emailEx)
                        {
                            await QueueInvoiceDeliveryFailureAsync(newInvoiceId, null, targetEmail, emailEx.Message, conn);
                            emailNotice = $" Email delivery failed ({emailEx.Message}). Invoice email queued for background retry.";
                        }
                    }
                }

                return Ok(new
                {
                    isSuccessful = true,
                    message = "Custom invoice created successfully." + emailNotice,
                    invoiceId = newInvoiceId,
                    invoiceNumber = invoiceNumber,
                    grandTotal = grandTotal
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [HttpPost("api/invoice/{id:int}/send-email")]
        public async Task<IActionResult> SendInvoiceEmail(int id)
        {
            try
            {
                using var conn = new SqlConnection(GetConnectionString());
                await conn.OpenAsync();

                string invoiceNumber = "";
                string targetEmail = "";
                string customerName = "";
                string spaceName = "WorkNest Workspace";
                decimal grandTotal = 0, subTotal = 0, taxTotal = 0, discountTotal = 0;
                DateTime issuedOn = DateTime.Today, dueOn = DateTime.Today;
                bool found = false;

                using (var cmd = new SqlCommand("dbo.WN_GetInvoiceEmailData", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@InvoiceId", id);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            found = true;
                            invoiceNumber = reader.GetString(reader.GetOrdinal("InvoiceNumber"));
                            targetEmail = reader.IsDBNull(reader.GetOrdinal("TargetEmail")) ? "" : reader.GetString(reader.GetOrdinal("TargetEmail"));
                            customerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? "Valued Customer" : reader.GetString(reader.GetOrdinal("CustomerName"));
                            spaceName = reader.IsDBNull(reader.GetOrdinal("SpaceName")) ? "WorkNest Workspace" : reader.GetString(reader.GetOrdinal("SpaceName"));
                            grandTotal = reader.GetDecimal(reader.GetOrdinal("GrandTotal"));
                            subTotal = reader.GetDecimal(reader.GetOrdinal("SubTotal"));
                            taxTotal = reader.GetDecimal(reader.GetOrdinal("TaxTotal"));
                            discountTotal = reader.GetDecimal(reader.GetOrdinal("DiscountTotal"));
                            issuedOn = reader.GetDateTime(reader.GetOrdinal("IssuedOn"));
                            dueOn = reader.GetDateTime(reader.GetOrdinal("DueOn"));
                        }
                    }
                }

                if (!found)
                    return NotFound(new { isSuccessful = false, message = "Invoice not found." });

                if (string.IsNullOrWhiteSpace(targetEmail))
                    return BadRequest(new { isSuccessful = false, message = "Customer email is missing for this invoice." });

                byte[]? pdfBytes = null;
                try
                {
                    var pdfDto = await BuildStatementInvoicePdfDtoAsync(id, conn);
                    if (pdfDto != null)
                    {
                        pdfBytes = StatementInvoicePdfGenerator.GeneratePdf(pdfDto);
                    }
                }
                catch (Exception pdfEx)
                {
                    Console.WriteLine($"[PDF Error] StatementInvoicePdfGenerator failed for invoice {id}: {pdfEx}");
                }

                try
                {
                    await _email.SendChallanEmailAsync(
                        toEmail: targetEmail,
                        customerName: customerName,
                        challanNumber: invoiceNumber,
                        spaceName: spaceName,
                        billingPeriod: "Invoice Billing",
                        totalPayable: grandTotal,
                        startOn: issuedOn,
                        endOn: dueOn,
                        totalContractAmount: grandTotal,
                        currentCycleAmount: subTotal,
                        taxAmount: taxTotal,
                        discountAmount: discountTotal,
                        pdfBytes: pdfBytes
                    );

                    return Ok(new { isSuccessful = true, message = $"Invoice email sent successfully to {targetEmail} with 5-Page Statement PDF packet attached." });
                }
                catch (Exception emailEx)
                {
                    await QueueInvoiceDeliveryFailureAsync(id, null, targetEmail, emailEx.Message, conn);
                    return Ok(new { isSuccessful = false, message = $"Invoice email delivery failed ({emailEx.Message}). Queued for background retry." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [HttpPost("api/invoice/send-initial/{bookingId:int}")]
        [HttpPost("api/booking/{bookingId:int}/send-initial-invoice")]
        public async Task<IActionResult> SendInitialInvoiceForBooking(int bookingId)
        {
            try
            {
                using var conn = new SqlConnection(GetConnectionString());
                await conn.OpenAsync();

                string checkSql = "SELECT TOP 1 Id, InvoiceNumber FROM dbo.WN_Invoices WHERE BookingId = @BookingId ORDER BY Id ASC;";
                using (var checkCmd = new SqlCommand(checkSql, conn))
                {
                    checkCmd.Parameters.AddWithValue("@BookingId", bookingId);
                    using var r = await checkCmd.ExecuteReaderAsync();
                    if (await r.ReadAsync())
                    {
                        int existingId = r.GetInt32(0);
                        r.Close();
                        return await SendInvoiceEmail(existingId);
                    }
                }

                string bookingSql = @"
                    SELECT TOP 1 
                        b.Id AS BookingId, b.UserId, b.StartOn, b.EndOn, b.MonthlyRent, b.SubtotalAmount, b.TotalAmount,
                        b.BillingPeriodMonths, b.SecurityDepositMonths, b.DiscountAmount, b.DiscountPercentage, b.DiscountType,
                        b.SecurityDepositRequired,
                        u.Email AS CustomerEmail, ISNULL(NULLIF(c.Company, ''), ISNULL(NULLIF(u.Name, ''), 'Valued Customer')) AS CustomerName,
                        s.Name AS SpaceName
                    FROM dbo.WN_Bookings b WITH (NOLOCK)
                    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = b.UserId
                    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = b.UserId
                    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                    WHERE b.Id = @BookingId;";

                int userId = 0;
                decimal monthlyRent = 0, discountAmount = 0, discountPercentage = 0, totalAmount = 0, secDepositReq = 0;
                int billingMonths = 3, secMonths = 2;
                string discountType = "Percentage";
                DateTime? startOn = null, endOn = null;
                string customerEmail = "", customerName = "", spaceName = "Workspace";

                using (var bCmd = new SqlCommand(bookingSql, conn))
                {
                    bCmd.Parameters.AddWithValue("@BookingId", bookingId);
                    using var reader = await bCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        userId = reader.GetInt32(reader.GetOrdinal("UserId"));
                        startOn = reader.IsDBNull(reader.GetOrdinal("StartOn")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("StartOn"));
                        endOn = reader.IsDBNull(reader.GetOrdinal("EndOn")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("EndOn"));
                        monthlyRent = reader.IsDBNull(reader.GetOrdinal("MonthlyRent")) ? 0 : reader.GetDecimal(reader.GetOrdinal("MonthlyRent"));
                        totalAmount = reader.IsDBNull(reader.GetOrdinal("TotalAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("TotalAmount"));
                        discountAmount = reader.IsDBNull(reader.GetOrdinal("DiscountAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("DiscountAmount"));
                        discountPercentage = reader.IsDBNull(reader.GetOrdinal("DiscountPercentage")) ? 0 : reader.GetDecimal(reader.GetOrdinal("DiscountPercentage"));
                        discountType = reader.IsDBNull(reader.GetOrdinal("DiscountType")) ? "Percentage" : reader.GetString(reader.GetOrdinal("DiscountType"));
                        secDepositReq = reader.IsDBNull(reader.GetOrdinal("SecurityDepositRequired")) ? 0 : reader.GetDecimal(reader.GetOrdinal("SecurityDepositRequired"));
                        billingMonths = reader.IsDBNull(reader.GetOrdinal("BillingPeriodMonths")) ? 3 : reader.GetInt32(reader.GetOrdinal("BillingPeriodMonths"));
                        secMonths = reader.IsDBNull(reader.GetOrdinal("SecurityDepositMonths")) ? 2 : reader.GetInt32(reader.GetOrdinal("SecurityDepositMonths"));
                        customerEmail = reader.IsDBNull(reader.GetOrdinal("CustomerEmail")) ? "" : reader.GetString(reader.GetOrdinal("CustomerEmail"));
                        customerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? "Valued Customer" : reader.GetString(reader.GetOrdinal("CustomerName"));
                        spaceName = reader.IsDBNull(reader.GetOrdinal("SpaceName")) ? "Workspace" : reader.GetString(reader.GetOrdinal("SpaceName"));
                    }
                    else
                    {
                        return NotFound(new { isSuccessful = false, message = "Booking not found." });
                    }
                }

                if (monthlyRent <= 0 && totalAmount > 0)
                {
                    monthlyRent = Math.Round(totalAmount / 12m, 2);
                }

                DateTime periodStart = startOn ?? DateTime.Today;
                DateTime periodEnd = periodStart.AddMonths(billingMonths).AddDays(-1);

                int contractMonths = (startOn.HasValue && endOn.HasValue && (endOn.Value - startOn.Value).TotalDays > 20) 
                    ? Math.Max(1, (int)Math.Round((endOn.Value - startOn.Value).TotalDays / 30.4375)) 
                    : 12;

                decimal grossAdvanceRent = monthlyRent * billingMonths;
                decimal appliedDiscount = 0m;
                if (discountPercentage > 0)
                {
                    appliedDiscount = Math.Round(grossAdvanceRent * (discountPercentage / 100.0m), 2);
                }
                else if (discountAmount > 0)
                {
                    if (discountAmount > grossAdvanceRent && contractMonths > billingMonths)
                    {
                        appliedDiscount = Math.Round(discountAmount * ((decimal)billingMonths / contractMonths), 2);
                    }
                    else
                    {
                        appliedDiscount = discountAmount;
                    }
                }

                bool isDepositRequired = secDepositReq > 0 || secMonths > 0;
                decimal securityDeposit = isDepositRequired ? (monthlyRent * secMonths) : 0m;

                var dto = new CreateCustomInvoiceDto
                {
                    BookingId = bookingId,
                    UserId = userId,
                    IssuedOn = DateTime.Today,
                    DueOn = DateTime.Today.AddDays(15),
                    Notes = $"Initial Payment Invoice for Booking #{bookingId} - {spaceName}",
                    SendEmail = true,
                    BillingPeriodStart = periodStart,
                    BillingPeriodEnd = periodEnd,
                    AdvanceRentMonths = billingMonths,
                    SecurityDepositAmount = securityDeposit,
                    Lines = new List<CreateCustomInvoiceLineDto>()
                };

                if (grossAdvanceRent > 0)
                {
                    dto.Lines.Add(new CreateCustomInvoiceLineDto
                    {
                        Description = $"Advance Rent for {periodStart:MMMM d, yyyy} to {periodEnd:MMMM d, yyyy}",
                        Quantity = 1,
                        UnitPrice = grossAdvanceRent,
                        DiscountAmount = appliedDiscount,
                        TaxRate = 0.016m,
                        ChargeTypeId = 1
                    });
                }

                if (securityDeposit > 0)
                {
                    dto.Lines.Add(new CreateCustomInvoiceLineDto
                    {
                        Description = $"Security Deposit ({secMonths} Month(s) Refundable - {spaceName})",
                        Quantity = 1,
                        UnitPrice = securityDeposit,
                        DiscountAmount = 0,
                        TaxRate = 0,
                        ChargeTypeId = 2
                    });
                }

                if (dto.Lines.Count == 0)
                {
                    dto.Lines.Add(new CreateCustomInvoiceLineDto
                    {
                        Description = $"Initial Workspace Payment ({spaceName})",
                        Quantity = 1,
                        UnitPrice = totalAmount > 0 ? totalAmount : 50000,
                        DiscountAmount = 0,
                        TaxRate = 0.016m,
                        ChargeTypeId = 1
                    });
                }

                return await CreateCustomInvoice(dto, customerEmail);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [HttpPost("api/booking/{bookingId:int}/generate-next-invoice")]
        public async Task<IActionResult> GenerateNextInvoiceForBooking(int bookingId)
        {
            try
            {
                using var conn = new SqlConnection(GetConnectionString());
                await conn.OpenAsync();

                string bookingSql = @"
                    SELECT TOP 1 
                        b.Id AS BookingId, b.UserId, b.StartOn, b.EndOn, b.MonthlyRent, b.TotalAmount,
                        b.BillingPeriodMonths, b.DiscountAmount, b.DiscountPercentage, b.DiscountType,
                        u.Email AS CustomerEmail, ISNULL(NULLIF(c.Company, ''), ISNULL(NULLIF(u.Name, ''), 'Valued Customer')) AS CustomerName,
                        s.Name AS SpaceName
                    FROM dbo.WN_Bookings b WITH (NOLOCK)
                    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = b.UserId
                    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = b.UserId
                    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                    WHERE b.Id = @BookingId;";

                int userId = 0;
                decimal monthlyRent = 0, discountAmount = 0, discountPercentage = 0, totalAmount = 0;
                int billingMonths = 3;
                DateTime? startOn = null, endOn = null;
                string customerEmail = "", customerName = "", spaceName = "Workspace";

                using (var bCmd = new SqlCommand(bookingSql, conn))
                {
                    bCmd.Parameters.AddWithValue("@BookingId", bookingId);
                    using var reader = await bCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        userId = reader.GetInt32(reader.GetOrdinal("UserId"));
                        startOn = reader.IsDBNull(reader.GetOrdinal("StartOn")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("StartOn"));
                        endOn = reader.IsDBNull(reader.GetOrdinal("EndOn")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("EndOn"));
                        monthlyRent = reader.IsDBNull(reader.GetOrdinal("MonthlyRent")) ? 0 : reader.GetDecimal(reader.GetOrdinal("MonthlyRent"));
                        totalAmount = reader.IsDBNull(reader.GetOrdinal("TotalAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("TotalAmount"));
                        discountAmount = reader.IsDBNull(reader.GetOrdinal("DiscountAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("DiscountAmount"));
                        discountPercentage = reader.IsDBNull(reader.GetOrdinal("DiscountPercentage")) ? 0 : reader.GetDecimal(reader.GetOrdinal("DiscountPercentage"));
                        billingMonths = reader.IsDBNull(reader.GetOrdinal("BillingPeriodMonths")) ? 3 : reader.GetInt32(reader.GetOrdinal("BillingPeriodMonths"));
                        customerEmail = reader.IsDBNull(reader.GetOrdinal("CustomerEmail")) ? "" : reader.GetString(reader.GetOrdinal("CustomerEmail"));
                        customerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? "Valued Customer" : reader.GetString(reader.GetOrdinal("CustomerName"));
                        spaceName = reader.IsDBNull(reader.GetOrdinal("SpaceName")) ? "Workspace" : reader.GetString(reader.GetOrdinal("SpaceName"));
                    }
                    else
                    {
                        return NotFound(new { isSuccessful = false, message = "Booking not found." });
                    }
                }

                DateTime periodStart = DateTime.Today;
                string lastEndSql = "SELECT TOP 1 BillingPeriodEnd FROM dbo.WN_Invoices WHERE BookingId = @BookingId ORDER BY Id DESC;";
                using (var lastEndCmd = new SqlCommand(lastEndSql, conn))
                {
                    lastEndCmd.Parameters.AddWithValue("@BookingId", bookingId);
                    var lastEndVal = await lastEndCmd.ExecuteScalarAsync();
                    if (lastEndVal != null && lastEndVal != DBNull.Value)
                    {
                        periodStart = ((DateTime)lastEndVal).AddDays(1);
                    }
                    else if (startOn.HasValue)
                    {
                        periodStart = startOn.Value;
                    }
                }

                DateTime periodEnd = periodStart.AddMonths(billingMonths).AddDays(-1);

                if (monthlyRent <= 0 && totalAmount > 0)
                {
                    monthlyRent = Math.Round(totalAmount / 12m, 2);
                }

                int contractMonths = (startOn.HasValue && endOn.HasValue && (endOn.Value - startOn.Value).TotalDays > 20) 
                    ? Math.Max(1, (int)Math.Round((endOn.Value - startOn.Value).TotalDays / 30.4375)) 
                    : 12;

                decimal grossAdvanceRent = monthlyRent * billingMonths;
                decimal appliedDiscount = 0m;
                if (discountPercentage > 0)
                {
                    appliedDiscount = Math.Round(grossAdvanceRent * (discountPercentage / 100.0m), 2);
                }
                else if (discountAmount > 0)
                {
                    if (discountAmount > grossAdvanceRent && contractMonths > billingMonths)
                    {
                        appliedDiscount = Math.Round(discountAmount * ((decimal)billingMonths / contractMonths), 2);
                    }
                    else
                    {
                        appliedDiscount = discountAmount;
                    }
                }

                decimal arrears = 0m;
                string arrearsSql = "SELECT ISNULL(SUM(GrandTotal - PaidTotal), 0) FROM dbo.WN_Invoices WHERE UserId = @UserId AND StatusId != 2;";
                using (var arrCmd = new SqlCommand(arrearsSql, conn))
                {
                    arrCmd.Parameters.AddWithValue("@UserId", userId);
                    var arrVal = await arrCmd.ExecuteScalarAsync();
                    if (arrVal != null && arrVal != DBNull.Value)
                    {
                        arrears = Convert.ToDecimal(arrVal);
                    }
                }

                var dto = new CreateCustomInvoiceDto
                {
                    BookingId = bookingId,
                    UserId = userId,
                    IssuedOn = DateTime.Today,
                    DueOn = DateTime.Today.AddDays(15),
                    Notes = $"Cycle Billing Invoice for Booking #{bookingId} - {spaceName}",
                    SendEmail = true,
                    BillingPeriodStart = periodStart,
                    BillingPeriodEnd = periodEnd,
                    AdvanceRentMonths = billingMonths,
                    SecurityDepositAmount = 0m,
                    Lines = new List<CreateCustomInvoiceLineDto>()
                };

                if (grossAdvanceRent > 0)
                {
                    dto.Lines.Add(new CreateCustomInvoiceLineDto
                    {
                        Description = $"Advance Rent for {periodStart:MMMM d, yyyy} to {periodEnd:MMMM d, yyyy}",
                        Quantity = 1,
                        UnitPrice = grossAdvanceRent,
                        DiscountAmount = appliedDiscount,
                        TaxRate = 0.016m,
                        ChargeTypeId = 1
                    });
                }

                if (arrears > 0)
                {
                    dto.Lines.Add(new CreateCustomInvoiceLineDto
                    {
                        Description = "Arrears from prior billing cycles",
                        Quantity = 1,
                        UnitPrice = arrears,
                        DiscountAmount = 0,
                        TaxRate = 0,
                        ChargeTypeId = 5
                    });
                }

                return await CreateCustomInvoice(dto, customerEmail);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        private async Task QueueInvoiceDeliveryFailureAsync(int invoiceId, int? bookingId, string? targetEmail, string lastError, SqlConnection conn)
        {
            try
            {
                string sql = @"
                    INSERT INTO dbo.WN_InvoiceDeliveryQueue (InvoiceId, BookingId, TargetEmail, Attempts, LastAttemptAt, NextRetryAt, Status, LastError, CreatedOn)
                    VALUES (@InvoiceId, @BookingId, @TargetEmail, 1, SYSUTCDATETIME(), DATEADD(minute, 5, SYSUTCDATETIME()), 'Pending', @LastError, SYSUTCDATETIME());";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@InvoiceId", invoiceId);
                cmd.Parameters.AddWithValue("@BookingId", (object?)bookingId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TargetEmail", (object?)targetEmail ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@LastError", lastError);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Delivery Queue Error] Failed to insert queue item for invoice {invoiceId}: {ex.Message}");
            }
        }
    }

    public class RecordPaymentDto
    {
        public decimal PaidAmount { get; set; }
        public string? PaymentMethod { get; set; }
        public string? TransactionRef { get; set; }
        public string? Notes { get; set; }
    }

    public class CreateCustomInvoiceDto
    {
        public int? BookingId { get; set; }
        public int UserId { get; set; }
        public DateTime IssuedOn { get; set; } = DateTime.Today;
        public DateTime DueOn { get; set; } = DateTime.Today.AddDays(15);
        public string? CurrencyCode { get; set; } = "PKR";
        public string? Notes { get; set; }
        public bool SendEmail { get; set; } = true;
        public DateTime? BillingPeriodStart { get; set; }
        public DateTime? BillingPeriodEnd { get; set; }
        public int? AdvanceRentMonths { get; set; }
        public decimal? SecurityDepositAmount { get; set; }
        public List<CreateCustomInvoiceLineDto> Lines { get; set; } = new();
    }

    public class CreateCustomInvoiceLineDto
    {
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; }
        public decimal DiscountAmount { get; set; } = 0;
        public decimal TaxRate { get; set; } = 0.16m;
        public byte ChargeTypeId { get; set; } = 1;
    }
}
