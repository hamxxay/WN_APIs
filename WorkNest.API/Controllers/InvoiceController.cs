using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.DTOs.InvoiceApproval;
using WorkNest.Application.DTOs.Lists;
using WorkNest.Application.DTOs.Payment;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Infrastructure.ExternalServices.Pdf;
using WorkNest.Common.Responses;

using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using Microsoft.AspNetCore.RateLimiting;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    [ValidateLocationScope]
    [RecordScope(RecordKind.Invoice, "id", "publicId")] // location-bound staff: only invoices of their locations
    public class InvoiceController : ControllerBase
    {
        /// <summary>Every invoice is due this many days after its invoice date (the invoice date is the day it is issued).</summary>
        public const int InvoiceDueDays = 7;

        private readonly IPaymentService _payments;
        private readonly IBookingService _bookings;
        private readonly IDbRepository _db;
        private readonly IPdfService _pdf;
        private readonly IConfiguration _config;
        private readonly IEmailService _email;
        private readonly IBusinessClock _clock;
        private readonly IOneBillService _oneBill;

        public InvoiceController(IPaymentService payments, IBookingService bookings, IDbRepository db, IPdfService pdf, IConfiguration config, IEmailService email, IBusinessClock clock,
            IOneBillService oneBill)
        {
            _clock = clock;
            _oneBill = oneBill;
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

        private async Task<SqlConnection> OpenConnectionAsync()
        {
            var conn = new SqlConnection(GetConnectionString());
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conn);
            await cmd.ExecuteNonQueryAsync();
            return conn;
        }

        // Staff = admin / super admin / sales executive / receptionist; customers (role "general") are not staff.
        private const string StaffRoles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive";

        // Super admin only (revert an approved invoice to Pending Approval).
        private const string SuperAdminRoles = "super_admin,SuperAdmin";

        /// <summary>Signed-in user's email from the JWT only (the x-user-email header is no longer trusted).</summary>
        private string? ResolveUserEmail() => User.GetEmail();

        // status (Unpaid / Partial / Paid / Overdue / Cancelled as the list shows it, from api/invoice/statuses), from / to
        // (issue date, both included): only then the LST_01 procedure is used; without them the list is read as before.
        // The CMS Invoices page sends the current month by default. Before LST_01 has run the filtered list is read from
        // the old procedure and filtered in the API (ListFilterService), so that page keeps working.
        [HttpGet("api/invoice")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> List(
            [FromServices] IListFilterService lists,
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null,
            [FromQuery] int? typeId = null,
            [FromQuery] int? locationId = null,
            [FromQuery] string? status = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            var filter = new ListFilter { Status = status, From = from, To = to };
            if (filter.HasStatusOrDates)
                return await ListFilterResults.RunAsync(async () =>
                {
                    if (page <= 0) page = 1;
                    if (limit <= 0) limit = 10;
                    var (rows, count) = await MultiLocationList.FetchAsync(User.ScopedLocations(locationId), page, limit,
                        (p, l, loc) => lists.GetInvoicesAsync(p, l, search, typeId, loc, filter));
                    return Ok(new { isSuccessful = true, data = rows, totalCount = count, page = page, limit = limit });
                });

            try
            {
                if (page <= 0) page = 1;
                if (limit <= 0) limit = 10;
                int offset = (page - 1) * limit;

                async Task<(IEnumerable<Dictionary<string, object?>> Items, int Total)> FetchPage(int pg, int lim, int? locId)
                {
                    using var conn = await OpenConnectionAsync();
                    using var cmd = new SqlCommand("dbo.WN_GetInvoicesList", conn);
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Page", pg);
                    cmd.Parameters.AddWithValue("@Limit", lim);
                    cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@TypeId", (object?)typeId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@LocationId", (object?)locId ?? DBNull.Value);

                    int count = 0;
                    var rows = new List<Dictionary<string, object?>>();
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        count = reader.GetInt32(0);
                    }
                    if (await reader.NextResultAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var row = new Dictionary<string, object?>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : WorkNest.Application.Services.DbDateTimeKinds.Normalize(reader.GetName(i), reader.GetValue(i));
                            }
                            rows.Add(row);
                        }
                    }
                    return (rows, count);
                }

                // Location-bound staff see every location they are assigned to (merged newest first).
                var (items, totalCount) = await MultiLocationList.FetchAsync(User.ScopedLocations(locationId), page, limit, FetchPage);

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

        /// <summary>Choices of the Invoices list Status filter (the statuses the list shows), [{ value, label }].</summary>
        [HttpGet("api/invoice/statuses")]
        [Authorize(Roles = StaffRoles)]
        public Task<IActionResult> ListStatuses([FromServices] IListFilterService lists) =>
            ListFilterResults.StatusOptionsAsync(lists, ListFilterRules.Invoices);

        /// <summary>
        /// The signed-in customer's bills (app Payments tab): their invoices (paid, unpaid, overdue, with the
        /// door-access suspension date) plus the booking challans that have no invoice, and the totals. Only the
        /// caller's own records (owner rule of GET api/invoice/{id}/pdf, so every invoice listed can be downloaded).
        /// </summary>
        [HttpGet("api/invoice/my")]
        public async Task<IActionResult> MyInvoices()
        {
            var email = ResolveUserEmail();
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "Sign in required." });
            try
            {
                var bills = await _bookings.GetMyBillsAsync(email);
                // One Bill number (One Bill locations) on the invoices still to pay.
                foreach (var bill in bills.Items.Where(b => b.Kind == "invoice" && b.InvoiceId > 0 && b.Balance >= 1m))
                    bill.OneBillNumber = await _oneBill.GetNumberAsync(bill.InvoiceId!.Value);
                return Ok(WorkNest.Common.Responses.ApiResponse.Ok(bills));
            }
            catch (Exception)
            {
                return StatusCode(500, new { isSuccessful = false, message = "Your bills could not be loaded. Please try again." });
            }
        }

        [HttpGet("api/customer/{customerId:int}/invoices")]
        [Authorize(Roles = StaffRoles)]
        [HttpGet("api/customers/{customerId:int}/invoices")]
        [RecordScope(RecordKind.Customer, "customerId")]
        public async Task<IActionResult> GetInvoicesByCustomerId(
            int customerId,
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] int? statusId = null)
        {
            try
            {
                if (customerId <= 0)
                    return BadRequest(new { isSuccessful = false, message = "Invalid customerId." });

                var (rows, total) = await _db.GetCustomerInvoicesDbAsync(customerId, 0, page, limit, statusId);

                return Ok(new
                {
                    isSuccessful = true,
                    data = rows,
                    totalCount = total,
                    page = page,
                    limit = limit
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [EnableRateLimiting("pdf")]
        [HttpGet("WorkNest/api/invoice/{id:int}/pdf")]
        [HttpGet("WorkNest/api/invoice/{id:int}/statement-pdf")]
        [HttpGet("WorkNest/api/invoice/{id:int}/download")]
        [HttpGet("api/invoice/{id:int}/pdf")]
        [HttpGet("api/invoice/{id:int}/statement-pdf")]
        [HttpGet("api/invoice/{id:int}/download")]
        public async Task<IActionResult> GetStatementInvoicePdf(int id, [FromServices] IDbRepository db)
        {
            // Staff see every invoice; a customer only their own (was anonymous: anyone could read any invoice by number).
            bool isStaff = User.IsInRole("admin") || User.IsInRole("Admin") || User.IsInRole("super_admin") || User.IsInRole("SuperAdmin")
                || User.IsInRole("sales_executive") || User.IsInRole("SalesExecutive") || User.IsInRole("receptionist") || User.IsInRole("Receptionist");
            if (!isStaff)
            {
                var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
                if (string.IsNullOrWhiteSpace(email) || !await db.IsInvoiceOwnedByDbAsync(id, email))
                    return NotFound(new { isSuccessful = false, message = "Invoice not found." });

                using var chkConn = await OpenConnectionAsync();
                using var chkCmd = new SqlCommand("SELECT ApprovalStatus FROM dbo.WN_Invoices WITH (NOLOCK) WHERE Id = @Id", chkConn);
                chkCmd.Parameters.AddWithValue("@Id", id);
                var appStatus = (await chkCmd.ExecuteScalarAsync())?.ToString();
                if (string.Equals(appStatus, "PendingApproval", StringComparison.OrdinalIgnoreCase))
                    return NotFound(new { isSuccessful = false, message = "Invoice not found." });
            }
            try
            {
                using var conn = await OpenConnectionAsync();

                var dto = await BuildStatementInvoicePdfDtoAsync(id, conn);
                if (dto == null)
                    return NotFound(new { isSuccessful = false, message = "Invoice not found." });

                if (string.IsNullOrWhiteSpace(dto.OneBillNumber))
                {
                    dto.OneBillNumber = await _oneBill.GetNumberAsync(id);
                }

                byte[] pdfBytes = StatementInvoicePdfGenerator.GeneratePdf(dto);
                string fileName = $"Statement-Invoice-{dto.InvoiceNumber}.pdf";
                if (Request.Path.Value?.Contains("/download", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return File(pdfBytes, "application/pdf", fileName);
                }

                Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
                return File(pdfBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [EnableRateLimiting("pdf")]
        [HttpGet("WorkNest/api/invoice/sales-tax/{publicId:guid}/pdf")]
        [HttpGet("WorkNest/api/invoice/sales-tax/{publicId:guid}/download")]
        [HttpGet("api/invoice/sales-tax/{publicId:guid}/pdf")]
        [HttpGet("api/invoice/sales-tax/{publicId:guid}/download")]
        public async Task<IActionResult> GetSalesTaxPdf(Guid publicId)
        {
            // Was anonymous: anyone holding the link could read the invoice (and opening it created the sales-tax
            // invoice). Now staff (location-bound staff via [RecordScope]) or the customer the invoice belongs to.
            // Invoice PDFs carry the sales-tax invoice as extra pages instead of this link.
            var invoiceId = await ResolveInvoiceIdForSalesTaxAsync(publicId);
            if (invoiceId == null)
                return NotFound(new { isSuccessful = false, message = "Sales Tax Invoice not found." });
            if (!User.IsStaff())
            {
                var email = User.GetEmail();
                if (string.IsNullOrWhiteSpace(email) || !await _db.IsInvoiceOwnedByDbAsync(invoiceId.Value, email))
                    return NotFound(new { isSuccessful = false, message = "Sales Tax Invoice not found." });
            }

            try
            {
                var dto = await _db.GetCustomerSTInvoiceByPublicIdAsync(publicId);
                if (dto == null)
                {
                    using var conn = await OpenConnectionAsync();

                    using var cmdFind = new SqlCommand("dbo.WN_GetInvoiceForSTGeneration", conn);
                    cmdFind.CommandType = CommandType.StoredProcedure;
                    cmdFind.Parameters.AddWithValue("@PublicId", publicId);
                    using var rFind = await cmdFind.ExecuteReaderAsync();
                    if (await rFind.ReadAsync())
                    {
                        int invId = rFind.GetInt32(rFind.GetOrdinal("Id"));
                        string invNum = rFind.GetString(rFind.GetOrdinal("InvoiceNumber"));
                        decimal subTotal = rFind.GetDecimal(rFind.GetOrdinal("SubTotal"));
                        decimal discTotal = rFind.GetDecimal(rFind.GetOrdinal("DiscountTotal"));
                        decimal taxTotal = rFind.GetDecimal(rFind.GetOrdinal("TaxTotal"));

                        rFind.Close();

                        string stInvoiceNumber = invNum.Replace("INV-", "ST-INV-");
                        decimal roomRentExcl = subTotal - discTotal;
                        decimal stTaxRate = taxTotal > 0 ? 16.00m : 0.00m;
                        decimal serviceChargeAmount = taxTotal > 0 ? Math.Round(taxTotal / 0.16m, 2) : 0m;
                        decimal roomRentAmount = Math.Max(0, roomRentExcl - serviceChargeAmount);
                        decimal stSubTotal = serviceChargeAmount;
                        decimal stGrandTotal = stSubTotal + taxTotal;

                        using var stCmd = new SqlCommand("dbo.WN_EnsureCustomerSTInvoice", conn);
                        stCmd.CommandType = CommandType.StoredProcedure;
                        stCmd.Parameters.AddWithValue("@InvoiceId", invId);
                        stCmd.Parameters.AddWithValue("@PublicId", (object?)publicId ?? DBNull.Value);
                        stCmd.Parameters.AddWithValue("@STInvoiceNumber", stInvoiceNumber);
                        stCmd.Parameters.AddWithValue("@RoomRentAmount", roomRentAmount);
                        stCmd.Parameters.AddWithValue("@ServiceChargeAmount", serviceChargeAmount);
                        stCmd.Parameters.AddWithValue("@STTaxRate", stTaxRate);
                        stCmd.Parameters.AddWithValue("@SubTotal", stSubTotal);
                        stCmd.Parameters.AddWithValue("@TaxAmount", taxTotal);
                        stCmd.Parameters.AddWithValue("@GrandTotal", stGrandTotal);
                        await stCmd.ExecuteNonQueryAsync();

                        dto = await _db.GetCustomerSTInvoiceByPublicIdAsync(publicId);
                    }
                }

                if (dto == null)
                {
                    return NotFound(new { isSuccessful = false, message = "Sales Tax Invoice not found." });
                }

                byte[] pdfBytes = _pdf.GenerateSalesTaxInvoicePdf(dto);

                string fileName = $"{dto.STInvoiceNumber}.pdf";
                if (Request.Path.Value?.Contains("/download", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return File(pdfBytes, "application/pdf", fileName);
                }

                Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
                return File(pdfBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        /// <summary>The invoice behind a sales-tax link GUID: a sales-tax invoice's PublicId or the invoice's own PublicId.</summary>
        private async Task<int?> ResolveInvoiceIdForSalesTaxAsync(Guid publicId)
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new SqlCommand(@"
                SELECT TOP 1 Id FROM (
                    SELECT st.CustomerInvoiceId AS Id, 0 AS Pref FROM dbo.WN_CustomerSTInvoice st WITH (NOLOCK) WHERE st.PublicId = @Guid
                    UNION ALL
                    SELECT i.Id, 1 FROM dbo.WN_Invoices i WITH (NOLOCK) WHERE i.PublicId = @Guid) x
                ORDER BY Pref;", conn);
            cmd.Parameters.Add("@Guid", SqlDbType.UniqueIdentifier).Value = publicId;
            return await cmd.ExecuteScalarAsync() is int id ? id : null;
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

            using var cmd = new SqlCommand("dbo.WN_GetStatementInvoicePdfData", conn);
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
                    if (HasColumn(reader, "SubTotal") && !reader.IsDBNull(reader.GetOrdinal("SubTotal")))
                        dto.SubTotal = reader.GetDecimal(reader.GetOrdinal("SubTotal"));
                    if (HasColumn(reader, "DiscountTotal") && !reader.IsDBNull(reader.GetOrdinal("DiscountTotal")))
                        dto.DiscountTotal = reader.GetDecimal(reader.GetOrdinal("DiscountTotal"));
                    if (HasColumn(reader, "TaxTotal") && !reader.IsDBNull(reader.GetOrdinal("TaxTotal")))
                        dto.TaxTotal = reader.GetDecimal(reader.GetOrdinal("TaxTotal"));
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
                    if (HasColumn(reader, "AppliedChargePercentage") && !reader.IsDBNull(reader.GetOrdinal("AppliedChargePercentage")))
                        dto.AppliedChargePercentage = reader.GetDecimal(reader.GetOrdinal("AppliedChargePercentage"));
                    if (HasColumn(reader, "AppliedTaxPercentage") && !reader.IsDBNull(reader.GetOrdinal("AppliedTaxPercentage")))
                        dto.AppliedTaxPercentage = reader.GetDecimal(reader.GetOrdinal("AppliedTaxPercentage"));
                    if (HasColumn(reader, "SupportChargeAmount") && !reader.IsDBNull(reader.GetOrdinal("SupportChargeAmount")))
                        dto.SupportChargeAmount = reader.GetDecimal(reader.GetOrdinal("SupportChargeAmount"));
                    if (HasColumn(reader, "SecurityDepositAmount") && !reader.IsDBNull(reader.GetOrdinal("SecurityDepositAmount")))
                        dto.SecurityDepositAmount = reader.GetDecimal(reader.GetOrdinal("SecurityDepositAmount"));
                    if (HasColumn(reader, "InvoiceTypeId") && !reader.IsDBNull(reader.GetOrdinal("InvoiceTypeId")))
                    {
                        dto.InvoiceTypeId = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("InvoiceTypeId")));
                        dto.IsWhtInvoice = dto.InvoiceTypeId == 6;
                    }
                    if (HasColumn(reader, "WithholdingTaxRate") && !reader.IsDBNull(reader.GetOrdinal("WithholdingTaxRate")))
                        dto.WithholdingTaxRate = reader.GetDecimal(reader.GetOrdinal("WithholdingTaxRate"));
                    if (HasColumn(reader, "LocationId") && !reader.IsDBNull(reader.GetOrdinal("LocationId")))
                        dto.LocationId = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("LocationId")));
                    if (HasColumn(reader, "OneBillNumber") && !reader.IsDBNull(reader.GetOrdinal("OneBillNumber")))
                        dto.OneBillNumber = reader.GetString(reader.GetOrdinal("OneBillNumber"));
                    // The sales-tax invoice (STPublicId) is attached further down, after it is made sure to exist.
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
                            if (!desc.Contains("Refundable", StringComparison.OrdinalIgnoreCase))
                            {
                                desc = desc.Trim() + " (Refundable)";
                            }
                        }
                        else
                        {
                            lineFrom = dto.BillingPeriodStart ?? dto.InvoiceDate;
                            lineTo = dto.BillingPeriodEnd ?? dto.DueDate;
                        }

                        decimal qty = HasColumn(reader, "Quantity") && !reader.IsDBNull(reader.GetOrdinal("Quantity")) ? Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("Quantity"))) : 1m;
                        decimal unitPrice = HasColumn(reader, "UnitPrice") && !reader.IsDBNull(reader.GetOrdinal("UnitPrice")) ? Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("UnitPrice"))) : 0m;
                        
                        decimal priceExcl = reader.IsDBNull(reader.GetOrdinal("PriceExclVat")) ? 0m : reader.GetDecimal(reader.GetOrdinal("PriceExclVat"));
                        if (unitPrice == 0 && qty > 0) unitPrice = priceExcl / qty;

                        items.Add(new StatementInvoiceLineItemDto
                        {
                            Description = desc,
                            FromDate = lineFrom,
                            ToDate = lineTo,
                            Quantity = qty,
                            UnitPrice = unitPrice,
                            PriceExclVat = priceExcl,
                            VatAmount = reader.IsDBNull(reader.GetOrdinal("VatAmount")) ? 0m : reader.GetDecimal(reader.GetOrdinal("VatAmount")),
                            Category = HasColumn(reader, "CategoryName") && !reader.IsDBNull(reader.GetOrdinal("CategoryName")) ? reader.GetString(reader.GetOrdinal("CategoryName")) : "Recurring",
                            IsDeposit = isDeposit
                        });
                    }
                    if (!items.Any(i => i.IsDeposit || i.Description.Contains("Deposit", StringComparison.OrdinalIgnoreCase)) && dto.SecurityDepositAmount > 0)
                    {
                        items.Add(new StatementInvoiceLineItemDto
                        {
                            Description = "Security Deposit",
                            PriceExclVat = dto.SecurityDepositAmount,
                            VatAmount = 0m,
                            Category = "Security Deposit",
                            IsDeposit = true
                        });
                    }
                    if (items.Count > 0) dto.LineItems = items;
                }

                if (await reader.NextResultAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        dto.PreviousOutstandingBalance = reader.IsDBNull(reader.GetOrdinal("PriorBalance")) ? 0m : reader.GetDecimal(reader.GetOrdinal("PriorBalance"));
                        dto.PaymentReceived = reader.IsDBNull(reader.GetOrdinal("PaymentReceived")) ? 0m : reader.GetDecimal(reader.GetOrdinal("PaymentReceived"));
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

            // Same rule as the PDF (StatementInvoicePdfGenerator): the invoice's TaxTotal, else the lines' VAT.
            // Lines can carry no VAT while the invoice total does, which used to hide the Sales Tax Invoice link.
            decimal totalLineVat = dto.TaxTotal > 0 ? dto.TaxTotal : dto.LineItems.Sum(i => i.VatAmount);
            bool isRecurring = dto.IsRecurringInvoice;

            bool hasTax;
            if (isRecurring)
            {
                // Recurring invoices only have tax if TaxTotal > 0 or line VAT > 0.
                hasTax = totalLineVat > 0;
            }
            else
            {
                hasTax = totalLineVat > 0 
                       || (dto.AppliedTaxPercentage.HasValue && dto.AppliedTaxPercentage.Value > 0)
                       || (dto.SupportChargeAmount.HasValue && dto.SupportChargeAmount.Value > 0);
            }

            if (!hasTax)
            {
                if (isRecurring)
                {
                    dto.SupportChargeAmount = 0m;
                    dto.AppliedTaxPercentage = 0m;
                    dto.AppliedChargePercentage = 0m;
                }
                else if (totalLineVat == 0)
                {
                    dto.AppliedTaxPercentage = 0m;
                    dto.AppliedChargePercentage = 0m;
                }
                dto.SalesTaxInvoice = null;
            }
            else
            {
                var stPublicId = await _db.GetSTInvoicePublicIdByInvoiceIdAsync(id);
                if (!stPublicId.HasValue)
                {
                    string stInvoiceNumber = dto.InvoiceNumber.Replace("INV-", "ST-INV-");
                    decimal roomRentExcl = dto.LineItems.Where(i => !i.IsDeposit).Sum(i => i.PriceExclVat);
                    decimal stTaxRate = totalLineVat > 0 ? 16.00m : 0.00m;
                    decimal serviceChargeAmount = totalLineVat > 0 ? Math.Round(totalLineVat / 0.16m, 2) : 0m;
                    decimal roomRentAmount = Math.Max(0, roomRentExcl - serviceChargeAmount);
                    decimal stSubTotal = serviceChargeAmount;
                    decimal stGrandTotal = stSubTotal + totalLineVat;

                    using var autoStCmd = new SqlCommand("dbo.WN_EnsureCustomerSTInvoice", conn);
                    autoStCmd.CommandType = CommandType.StoredProcedure;
                    autoStCmd.Parameters.AddWithValue("@InvoiceId", id);
                    autoStCmd.Parameters.AddWithValue("@PublicId", DBNull.Value);
                    autoStCmd.Parameters.AddWithValue("@STInvoiceNumber", stInvoiceNumber);
                    autoStCmd.Parameters.AddWithValue("@RoomRentAmount", roomRentAmount);
                    autoStCmd.Parameters.AddWithValue("@ServiceChargeAmount", serviceChargeAmount);
                    autoStCmd.Parameters.AddWithValue("@STTaxRate", stTaxRate);
                    autoStCmd.Parameters.AddWithValue("@SubTotal", stSubTotal);
                    autoStCmd.Parameters.AddWithValue("@TaxAmount", totalLineVat);
                    autoStCmd.Parameters.AddWithValue("@GrandTotal", stGrandTotal);
                    await autoStCmd.ExecuteNonQueryAsync();

                    stPublicId = await _db.GetSTInvoicePublicIdByInvoiceIdAsync(id);
                }

                // The sales-tax invoice for support services goes into the same PDF as extra pages. It used to be a link to
                // an anonymous download, so anyone the PDF was forwarded to could open it.
                dto.SalesTaxInvoice = stPublicId.HasValue && hasTax
                    ? await _db.GetCustomerSTInvoiceByPublicIdAsync(stPublicId.Value)
                    : null;

                // Reconcile SalesTaxInvoice for recurring invoices if loaded
                if (isRecurring && dto.SalesTaxInvoice != null)
                {
                    decimal cycleTax = totalLineVat;
                    decimal cycleServiceCharges = dto.SupportChargeAmount.HasValue && dto.SupportChargeAmount.Value > 0
                        ? dto.SupportChargeAmount.Value
                        : Math.Round(cycleTax / 0.16m, 2);

                    dto.SalesTaxInvoice.TaxTotal = cycleTax;
                    dto.SalesTaxInvoice.ServiceChargeTaxAmount = cycleTax;
                    dto.SalesTaxInvoice.ServiceChargeAmount = cycleServiceCharges;
                    dto.SalesTaxInvoice.SubTotal = cycleServiceCharges;
                    dto.SalesTaxInvoice.GrandTotal = cycleServiceCharges + cycleTax;
                }
            }

            dto.OneBillNumber = await _oneBill.GetNumberAsync(id);
            return dto;
        }

        [HttpGet("api/invoice/{id:int}")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> GetDetails(int id)
        {
            try
            {
                using var conn = await OpenConnectionAsync();

                using var cmd = new SqlCommand("dbo.WN_GetInvoiceDetailsById", conn);
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
                            invoice[reader.GetName(i)] = reader.IsDBNull(i) ? null : WorkNest.Application.Services.DbDateTimeKinds.Normalize(reader.GetName(i), reader.GetValue(i));
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
                                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : WorkNest.Application.Services.DbDateTimeKinds.Normalize(reader.GetName(i), reader.GetValue(i));
                            }
                            lines.Add(row);
                        }
                    }
                }

                // WN_GetInvoiceDetailsById has no StatusLabel: the same label the invoice list (WN_GetInvoicesList) shows.
                if (!invoice.ContainsKey("StatusLabel"))
                {
                    using var labelCmd = new SqlCommand(@"
                        SELECT COALESCE(
                            -- legacy values on older rows
                            CASE i.StatusId WHEN 1 THEN 'Unpaid' WHEN 2 THEN 'Paid' WHEN 3 THEN 'Partial' WHEN 4 THEN 'Overdue' WHEN 5 THEN 'Cancelled' END,
                            -- OrderStatus IDs (looked up, not hard-coded)
                            (SELECT CASE LTRIM(RTRIM(os.Description)) WHEN 'Un Paid' THEN 'Unpaid' WHEN 'Challan Expire' THEN 'Overdue' ELSE LTRIM(RTRIM(os.Description)) END
                               FROM dbo.OrderStatus os WITH (NOLOCK) WHERE os.Id = i.StatusId),
                            'Unknown') AS StatusLabel
                          FROM dbo.WN_Invoices i WITH (NOLOCK)
                         WHERE i.Id = @InvoiceId;", conn);
                    labelCmd.Parameters.Add("@InvoiceId", SqlDbType.Int).Value = id;
                    var label = await labelCmd.ExecuteScalarAsync();
                    invoice["StatusLabel"] = label is null or DBNull ? null : label.ToString();
                }

                // Only once the invoice exists on EzPay (the raw column is set as soon as it is being sent).
                invoice["OneBillNumber"] = await _oneBill.GetNumberAsync(id);
                invoice["lines"] = lines;
                return Ok(new { isSuccessful = true, data = invoice });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Soft deletes an invoice by setting StatusId to 40 (InvoiceStatus.Deleted).
        /// </summary>
        [HttpDelete("api/invoice/{id:int}")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> SoftDelete(int id)
        {
            if (id <= 0) return BadRequest(new { isSuccessful = false, message = "Invalid invoice ID." });

            try
            {
                using var conn = await OpenConnectionAsync();
                using var cmd = new SqlCommand(@"
                    UPDATE dbo.WN_Invoices 
                    SET StatusId = 40, UpdatedOn = SYSUTCDATETIME() 
                    WHERE Id = @Id;
                    SELECT @@ROWCOUNT;", conn);
                cmd.Parameters.AddWithValue("@Id", id);

                var affected = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (affected == 0)
                    return NotFound(new { isSuccessful = false, message = "Invoice not found." });

                return Ok(new { isSuccessful = true, message = "Invoice deleted successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Soft deletes an invoice by public GUID by setting StatusId to 40 (InvoiceStatus.Deleted).
        /// </summary>
        [HttpDelete("api/invoice/{publicId:guid}")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> SoftDeleteByGuid(Guid publicId)
        {
            try
            {
                using var conn = await OpenConnectionAsync();
                using var cmd = new SqlCommand(@"
                    UPDATE dbo.WN_Invoices 
                    SET StatusId = 40, UpdatedOn = SYSUTCDATETIME() 
                    WHERE PublicId = @PublicId;
                    SELECT @@ROWCOUNT;", conn);
                cmd.Parameters.AddWithValue("@PublicId", publicId);

                var affected = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (affected == 0)
                    return NotFound(new { isSuccessful = false, message = "Invoice not found." });

                return Ok(new { isSuccessful = true, message = "Invoice deleted successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        // Record Payment on Invoices & Billing (cash, cheque, bank transfer; part or full). dbo.WN_RecordInvoicePayment
        // (INV_FIX_A) updates PaidTotal + status, re-activates the booking's access cards and writes the WN_Payments row.
        // [RecordScope] on the class keeps location-bound staff to invoices of their locations ("id").
        [HttpPost("api/invoice/{id:int}/record-payment")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> RecordPayment(int id, [FromBody] RecordPaymentDto req)
        {
            if (req == null || req.PaidAmount <= 0)
                return BadRequest(new { isSuccessful = false, message = "Payment amount must be greater than 0." });

            try
            {
                using var conn = await OpenConnectionAsync();
                using var cmd = new SqlCommand("dbo.WN_RecordInvoicePayment", conn) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@InvoiceId", id);
                cmd.Parameters.Add("@PaidAmount", SqlDbType.Decimal).Value = req.PaidAmount;
                cmd.Parameters["@PaidAmount"].Precision = 18;
                cmd.Parameters["@PaidAmount"].Scale = 4;
                cmd.Parameters.AddWithValue("@PaymentMethod", string.IsNullOrWhiteSpace(req.PaymentMethod) ? "Cash" : req.PaymentMethod.Trim());
                cmd.Parameters.AddWithValue("@TransactionRef", string.IsNullOrWhiteSpace(req.TransactionRef) ? DBNull.Value : req.TransactionRef.Trim());
                cmd.Parameters.AddWithValue("@Notes", string.IsNullOrWhiteSpace(req.Notes) ? DBNull.Value : req.Notes.Trim());

                decimal paidTotal = 0;
                int statusId = 0;
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        paidTotal = Convert.ToDecimal(reader["PaidTotal"]);
                        statusId = Convert.ToInt32(reader["StatusId"]);
                    }
                }
                return Ok(new { isSuccessful = true, message = "Payment recorded successfully", data = new { paidTotal, statusId } });
            }
            catch (SqlException ex) when (ex.Number == 50000)   // RAISERROR in the procedure: not found / bad amount
            {
                return ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? NotFound(new { isSuccessful = false, message = "Invoice not found." })
                    : BadRequest(new { isSuccessful = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [HttpPost("api/invoice/advance")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> CreateAdvance([FromBody] AdvanceInvoiceRequest request)
        {
            var email = ResolveUserEmail();
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity required" });

            if (!await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Booking, request.BookingId.ToString()))
                return NotFound(new { isSuccessful = false, message = "Booking not found." });

            var result = await _payments.CreateAdvanceInvoiceAsync(request, email);
            if (!result.IsSuccessful) return BadRequest(result);
            return StatusCode(201, result);
        }

        [HttpPost("api/invoice")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> CreateCustomInvoice([FromBody] CreateCustomInvoiceDto req)
        {
            try
            {
                if (req == null || req.UserId <= 0 || req.Lines == null || req.Lines.Count == 0)
                    return BadRequest(new { isSuccessful = false, message = "UserId and at least one line item are required." });

                // Location-bound staff: only for bookings / customers of their locations.
                var inScope = req.BookingId is > 0
                    ? await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Booking, req.BookingId.Value.ToString())
                    : await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.User, req.UserId.ToString());
                if (!inScope)
                    return NotFound(new { isSuccessful = false, message = "Customer not found." });

                using var conn = await OpenConnectionAsync();

                decimal subTotal = 0;
                decimal discountTotal = 0;
                decimal taxTotal = 0;
                decimal securityDepositAmount = req.SecurityDepositAmount ?? 0m;

                foreach (var line in req.Lines)
                {
                    if (line.ChargeTypeId == 2 || (!string.IsNullOrWhiteSpace(line.Description) && line.Description.Contains("Security Deposit", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!req.SecurityDepositAmount.HasValue || req.SecurityDepositAmount == 0)
                        {
                            securityDepositAmount += (line.Quantity * line.UnitPrice);
                        }
                    }
                    else
                    {
                        decimal lineNet = (line.Quantity * line.UnitPrice) - line.DiscountAmount;
                        decimal lineTax = Math.Round(lineNet * line.TaxRate, 2, MidpointRounding.AwayFromZero);
                        subTotal += (line.Quantity * line.UnitPrice);
                        discountTotal += line.DiscountAmount;
                        taxTotal += lineTax;
                    }
                }

                if (req.TaxOnServiceCharges.HasValue && req.TaxOnServiceCharges.Value > 0 && taxTotal == 0)
                {
                    taxTotal = req.TaxOnServiceCharges.Value;
                }
                else if (req.TaxTotal.HasValue && req.TaxTotal.Value > 0 && taxTotal == 0)
                {
                    taxTotal = req.TaxTotal.Value;
                }

                if (req.SubTotal.HasValue && req.SubTotal.Value > 0 && subTotal == 0)
                {
                    subTotal = req.SubTotal.Value;
                }

                // First / next invoices send the rent line already net of the discount and pass the discount
                // separately; custom invoices send gross lines with line discounts. Keep subTotal GROSS either way
                // (the invoice stores SubTotal before discount: GrandTotal = SubTotal - DiscountTotal + Tax + Deposit).
                bool linesNetOfDiscount = discountTotal == 0 && req.DiscountTotal.HasValue && req.DiscountTotal.Value > 0;
                if (linesNetOfDiscount)
                {
                    discountTotal = req.DiscountTotal!.Value;
                    subTotal = req.SubTotal.HasValue && req.SubTotal.Value > 0 ? req.SubTotal.Value : subTotal + discountTotal;
                }

                decimal serviceChargeAmount = req.ServiceCharges ?? (taxTotal > 0 ? Math.Round(taxTotal / 0.16m, 2, MidpointRounding.AwayFromZero) : 0m);
                decimal roomRentAmount = req.RoomRentExclTax ?? Math.Max(0m, (subTotal - discountTotal) - serviceChargeAmount);
                decimal taxOnServiceCharges = req.TaxOnServiceCharges ?? taxTotal;
                decimal grandTotal = req.GrandTotal ?? Math.Round((subTotal - discountTotal) + taxTotal + securityDepositAmount, 2, MidpointRounding.AwayFromZero);

                using var cmd = new SqlCommand("dbo.WN_CreateCustomInvoice", conn);
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
                cmd.Parameters.AddWithValue("@AdvanceRentMonths", (object?)req.AdvanceRentMonths ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@SecurityDepositMonths", (object?)req.SecurityDepositMonths ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@SecurityDepositAmount", securityDepositAmount);
                cmd.Parameters.AddWithValue("@BillingPeriodStart", (object?)req.BillingPeriodStart ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@BillingPeriodEnd", (object?)req.BillingPeriodEnd ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@AccountReceivableId", (object?)req.AccountReceivableId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RentAccountId", (object?)req.RentAccountId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ServicesIncomeId", (object?)req.ServicesIncomeId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@SalesTaxId", (object?)req.SalesTaxId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@SecurityReceivedId", (object?)req.SecurityReceivedId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@AccountsCoaId", (object?)req.AccountsCoaId ?? (object?)req.RentAccountId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RoomRentExclTax", roomRentAmount);
                cmd.Parameters.AddWithValue("@ServiceCharges", serviceChargeAmount);
                cmd.Parameters.AddWithValue("@TaxOnServiceCharges", taxOnServiceCharges);

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
                    using var lineCmd = new SqlCommand("dbo.WN_InsertInvoiceLine", conn);
                    lineCmd.CommandType = CommandType.StoredProcedure;
                    lineCmd.Parameters.AddWithValue("@InvoiceId", newInvoiceId);
                    // A "Security Deposit" line sent without a charge type is a deposit (2), never rent: WHT must not gross it up.
                    // (same test as the deposit total above; the line DTO defaults ChargeTypeId to 1).
                    bool isDepositLine = line.ChargeTypeId == 2 || (!string.IsNullOrWhiteSpace(line.Description)
                                         && line.Description.Contains("Security Deposit", StringComparison.OrdinalIgnoreCase));
                    lineCmd.Parameters.AddWithValue("@ChargeTypeId", isDepositLine ? (byte)2 : (line.ChargeTypeId > 0 ? line.ChargeTypeId : (byte)1));
                    lineCmd.Parameters.AddWithValue("@Description", string.IsNullOrWhiteSpace(line.Description) ? "Custom Charge" : line.Description);
                    lineCmd.Parameters.AddWithValue("@Quantity", line.Quantity > 0 ? line.Quantity : 1);
                    lineCmd.Parameters.AddWithValue("@UnitPrice", line.UnitPrice);
                    lineCmd.Parameters.AddWithValue("@DiscountAmount", line.DiscountAmount);
                    lineCmd.Parameters.AddWithValue("@TaxRate", line.TaxRate);
                    lineCmd.Parameters.AddWithValue("@SortOrder", sortOrder++);

                    await lineCmd.ExecuteNonQueryAsync();
                }

                using var updateCmd = new SqlCommand("dbo.WN_UpdateInvoiceBreakdown", conn);
                updateCmd.CommandType = CommandType.StoredProcedure;
                updateCmd.Parameters.AddWithValue("@InvoiceId", newInvoiceId);
                updateCmd.Parameters.AddWithValue("@SubTotal", subTotal);   // gross, before discount (was net minus the discount again)
                updateCmd.Parameters.AddWithValue("@DiscountTotal", discountTotal);
                updateCmd.Parameters.AddWithValue("@TaxTotal", taxTotal);
                updateCmd.Parameters.AddWithValue("@GrandTotal", grandTotal);
                updateCmd.Parameters.AddWithValue("@BillingPeriodStart", (object?)req.BillingPeriodStart ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@BillingPeriodEnd", (object?)req.BillingPeriodEnd ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@AdvanceRentMonths", (object?)req.AdvanceRentMonths ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@SecurityDepositMonths", (object?)req.SecurityDepositMonths ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@SecurityDepositAmount", securityDepositAmount);   // incl. a deposit taken from the lines
                updateCmd.Parameters.AddWithValue("@RoomRentExclTax", roomRentAmount);
                updateCmd.Parameters.AddWithValue("@ServiceCharges", serviceChargeAmount);
                updateCmd.Parameters.AddWithValue("@TaxOnServiceCharges", taxOnServiceCharges);
                updateCmd.Parameters.AddWithValue("@RentAccountId", (object?)req.RentAccountId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@SecurityReceivedId", (object?)req.SecurityReceivedId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@ServicesIncomeId", (object?)req.ServicesIncomeId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@SalesTaxId", (object?)req.SalesTaxId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@AccountReceivableId", (object?)req.AccountReceivableId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@AccountsCoaId", (object?)req.AccountsCoaId ?? (object?)req.RentAccountId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@DueOn", (object?)req.DueOn ?? DBNull.Value);
                await updateCmd.ExecuteNonQueryAsync();

                string stInvoiceNumber = invoiceNumber.Replace("INV-", "ST-INV-");
                decimal stTaxRate = taxTotal > 0 ? 16.00m : 0.00m;
                decimal stSubTotal = serviceChargeAmount;
                decimal stGrandTotal = stSubTotal + taxTotal;

                using var stCmd = new SqlCommand("dbo.WN_EnsureCustomerSTInvoice", conn);
                stCmd.CommandType = CommandType.StoredProcedure;
                stCmd.Parameters.AddWithValue("@InvoiceId", newInvoiceId);
                stCmd.Parameters.AddWithValue("@PublicId", DBNull.Value);
                stCmd.Parameters.AddWithValue("@STInvoiceNumber", stInvoiceNumber);
                stCmd.Parameters.AddWithValue("@RoomRentAmount", roomRentAmount);
                stCmd.Parameters.AddWithValue("@ServiceChargeAmount", serviceChargeAmount);
                stCmd.Parameters.AddWithValue("@STTaxRate", stTaxRate);
                stCmd.Parameters.AddWithValue("@SubTotal", stSubTotal);
                stCmd.Parameters.AddWithValue("@TaxAmount", taxTotal);
                stCmd.Parameters.AddWithValue("@GrandTotal", stGrandTotal);
                await stCmd.ExecuteNonQueryAsync();

                // One Bill location (e.g. F-7): create the invoice on EzPay now, so the PDF and email below carry its
                // One Bill number. Null for other locations, or when EzPay failed (the One Bill sync retries it).
                var oneBillNumber = await _oneBill.EnsureAsync(newInvoiceId);

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
                                if (!string.IsNullOrWhiteSpace(oneBillNumber)) pdfDto.OneBillNumber = oneBillNumber;
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
                                pdfBytes: pdfBytes,
                                oneBillNumber: oneBillNumber
                            );
                            emailNotice = $" Email dispatched with PDF attached to {targetEmail}.";
                        }
                        catch (EmailHeldException)
                        {
                            // Email Sending is off: held in Pending emails (not queued here too, or it would go out twice).
                            emailNotice = " Email sending is off: the invoice e-mail is held in Pending emails and goes out when it is turned on.";
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
                    grandTotal = grandTotal,
                    oneBillNumber = oneBillNumber
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        private async Task SetAuditContextAsync(SqlConnection conn)
        {
            var email = ResolveUserEmail();
            int? userId = null;
            var idClaim = User.FindFirst("id")?.Value ?? User.FindFirst("userId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(idClaim, out var parsedId) && parsedId > 0) userId = parsedId;

            try
            {
                using var setCtx = new SqlCommand(@"
                    DECLARE @ResolvedId INT = @UserId;
                    IF @ResolvedId IS NULL AND @UserEmail IS NOT NULL
                        SELECT TOP 1 @ResolvedId = Id FROM dbo.WN_Users WITH (NOLOCK) WHERE Email = @UserEmail;
                    IF @ResolvedId IS NOT NULL
                        EXEC sp_set_session_context @key = N'AppUserId', @value = @ResolvedId;
                    IF @UserEmail IS NOT NULL
                        EXEC sp_set_session_context @key = N'AppUserEmail', @value = @UserEmail;
                    EXEC sp_set_session_context @key = N'SourceApp', @value = N'WebAPI';", conn);
                setCtx.Parameters.Add("@UserId", SqlDbType.Int).Value = (object?)userId ?? DBNull.Value;
                setCtx.Parameters.Add("@UserEmail", SqlDbType.NVarChar, 256).Value = (object?)email ?? DBNull.Value;
                await setCtx.ExecuteNonQueryAsync();
            }
            catch { /* session context is best-effort */ }
        }

        private async Task<int?> ResolveUserIdAsync(SqlConnection conn)
        {
            var idClaim = User.FindFirst("id")?.Value ?? User.FindFirst("userId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(idClaim, out var parsedId) && parsedId > 0) return parsedId;

            var email = ResolveUserEmail();
            if (!string.IsNullOrWhiteSpace(email))
            {
                using var cmd = new SqlCommand("SELECT TOP 1 Id FROM dbo.WN_Users WITH (NOLOCK) WHERE Email = @Email", conn);
                cmd.Parameters.AddWithValue("@Email", email);
                var r = await cmd.ExecuteScalarAsync();
                if (r is int id) return id;
            }
            return null;
        }

        private async Task<(bool Success, string Message)> SendInvoiceEmailCoreAsync(int id, SqlConnection conn)
        {
            string invoiceNumber = "";
            string targetEmail = "";
            string customerName = "";
            string spaceName = "WorkNest Workspace";
            decimal grandTotal = 0, subTotal = 0, taxTotal = 0, discountTotal = 0, securityDeposit = 0;
            DateTime issuedOn = _clock.Today, dueOn = _clock.Today;
            bool found = false;

            using (var cmd = new SqlCommand("dbo.WN_GetInvoiceEmailData", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@InvoiceId", id);

                using var reader = await cmd.ExecuteReaderAsync();
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
                    decimal secDep = HasColumn(reader, "SecurityDepositAmount") && !reader.IsDBNull(reader.GetOrdinal("SecurityDepositAmount"))
                        ? reader.GetDecimal(reader.GetOrdinal("SecurityDepositAmount"))
                        : 0m;
                    securityDeposit = secDep;
                }
            }

            if (!found)
                return (false, "Invoice not found.");

            if (string.IsNullOrWhiteSpace(targetEmail))
                return (false, "Customer email is missing for this invoice.");

            // One Bill location: make sure the invoice is on EzPay before the PDF / email are made.
            var oneBillNumber = await _oneBill.EnsureAsync(id);

            byte[]? pdfBytes = null;
            try
            {
                var pdfDto = await BuildStatementInvoicePdfDtoAsync(id, conn);
                if (pdfDto != null)
                {
                    if (!string.IsNullOrWhiteSpace(oneBillNumber)) pdfDto.OneBillNumber = oneBillNumber;
                    pdfBytes = StatementInvoicePdfGenerator.GeneratePdf(pdfDto);
                    if (securityDeposit <= 0 && pdfDto.SecurityDepositAmount > 0)
                    {
                        securityDeposit = pdfDto.SecurityDepositAmount;
                    }
                }
                else
                {
                    Console.WriteLine($"[PDF Error] BuildStatementInvoicePdfDtoAsync returned null for invoice {id}.");
                }
            }
            catch (Exception pdfEx)
            {
                Console.WriteLine($"[PDF Error] StatementInvoicePdfGenerator failed for invoice {id}: {pdfEx}");
            }

            if (pdfBytes == null || pdfBytes.Length == 0)
            {
                await QueueInvoiceDeliveryFailureAsync(id, null, targetEmail, "Failed to generate Invoice PDF attachment.", conn);
                return (false, "Failed to generate Invoice PDF attachment.");
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
                    securityDeposit: securityDeposit,
                    taxAmount: taxTotal,
                    discountAmount: discountTotal,
                    pdfBytes: pdfBytes,
                    oneBillNumber: oneBillNumber
                );

                return (true, $"Invoice email sent successfully to {targetEmail} with Statement PDF attached.");
            }
            catch (EmailHeldException held)
            {
                return (false, held.Message);   // held in Pending emails: not queued for the invoice retry too
            }
            catch (Exception emailEx)
            {
                await QueueInvoiceDeliveryFailureAsync(id, null, targetEmail, emailEx.Message, conn);
                return (false, emailEx.Message);
            }
        }

        [EnableRateLimiting("pdf")]
        [HttpPost("api/invoice/{id:int}/send-email")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> SendInvoiceEmail(int id, [FromQuery] bool force = false)
        {
            try
            {
                using var conn = await OpenConnectionAsync();

                // Guard: check if invoice is PendingApproval
                using (var chkCmd = new SqlCommand("SELECT ApprovalStatus FROM dbo.WN_Invoices WITH (NOLOCK) WHERE Id = @Id", conn))
                {
                    chkCmd.Parameters.AddWithValue("@Id", id);
                    var statusVal = (await chkCmd.ExecuteScalarAsync())?.ToString();
                    if (string.Equals(statusVal, "PendingApproval", StringComparison.OrdinalIgnoreCase))
                    {
                        if (force)
                        {
                            var currentUserId = await ResolveUserIdAsync(conn);
                            using var appCmd = new SqlCommand(@"
                                UPDATE dbo.WN_Invoices 
                                SET ApprovalStatus = 'Approved', 
                                    ApprovedAt = SYSUTCDATETIME(), 
                                    ApprovedBy = @UserId,
                                    UpdatedOn = SYSUTCDATETIME()
                                WHERE Id = @Id", conn);
                            appCmd.Parameters.AddWithValue("@Id", id);
                            appCmd.Parameters.AddWithValue("@UserId", (object?)currentUserId ?? DBNull.Value);
                            await appCmd.ExecuteNonQueryAsync();
                        }
                        else
                        {
                            return BadRequest(new { isSuccessful = false, message = "This recurring invoice is pending approval and cannot be emailed until approved. Pass ?force=true to approve and send." });
                        }
                    }
                }

                var res = await SendInvoiceEmailCoreAsync(id, conn);
                if (!res.Success)
                {
                    return Ok(new { isSuccessful = false, message = $"Invoice email delivery failed ({res.Message}). Queued for background retry." });
                }

                return Ok(new { isSuccessful = true, message = res.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Sales executive approval queue: lists PendingApproval recurring invoices for the caller's assigned location(s).
        /// SuperAdmin may review any location or all locations.
        /// </summary>
        [HttpGet("api/invoice/approval-queue")]
        [Authorize(Roles = "sales_executive,SalesExecutive,admin,Admin,super_admin,SuperAdmin")]
        public async Task<IActionResult> GetApprovalQueue([FromQuery] int? locationId = null)
        {
            try
            {
                using var conn = await OpenConnectionAsync();
                var allowedLocations = User.ScopedLocations(locationId);

                if (User.IsLocationBoundRole() && (allowedLocations.Count == 0 || allowedLocations.Contains(-1)))
                {
                    return Ok(new { isSuccessful = true, data = new List<object>() });
                }

                var results = new List<Dictionary<string, object?>>();
                var locsToQuery = User.IsLocationBoundRole() ? allowedLocations : new List<int?> { locationId };

                foreach (var loc in locsToQuery)
                {
                    using var cmd = new SqlCommand("dbo.WN_Invoices_GetApprovalQueue", conn);
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@LocationId", (object?)loc ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@UserId", DBNull.Value);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object?>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : WorkNest.Application.Services.DbDateTimeKinds.Normalize(reader.GetName(i), reader.GetValue(i));
                        }
                        int invId = Convert.ToInt32(row["Id"]);
                        if (!results.Any(x => Convert.ToInt32(x["Id"]) == invId))
                        {
                            results.Add(row);
                        }
                    }
                }

                return Ok(new { isSuccessful = true, data = results });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Records that the logged-in user viewed the invoice details. Must be called before approval.
        /// </summary>
        [HttpPost("api/invoice/{id:int}/view")]
        [Authorize(Roles = "sales_executive,SalesExecutive,admin,Admin,super_admin,SuperAdmin")]
        [RecordScope(RecordKind.Invoice, "id")]
        public async Task<IActionResult> RecordInvoiceViewed(int id)
        {
            if (id <= 0) return BadRequest(new { isSuccessful = false, message = "Invalid invoice ID." });

            if (!await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Invoice, id.ToString()))
                return StatusCode(403, new { isSuccessful = false, message = "Location mismatch. You cannot view invoices outside your assigned location." });

            try
            {
                using var conn = await OpenConnectionAsync();
                var userId = await ResolveUserIdAsync(conn);
                if (userId == null)
                    return Unauthorized(new { isSuccessful = false, message = "User record not found." });

                await SetAuditContextAsync(conn);

                using var cmd = new SqlCommand("dbo.WN_Invoices_RecordViewed", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@InvoiceId", id);
                cmd.Parameters.AddWithValue("@UserId", userId.Value);

                var affected = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return Ok(new { isSuccessful = true, message = "Invoice marked as viewed.", viewed = affected > 0 });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Approves exactly one PendingApproval invoice.
        /// Rejects if not viewed, not PendingApproval, viewed by another user, or caller's location does not match.
        /// Super admins receive 403 Forbidden on approve (queue review only).
        /// Dispatches the email immediately upon approval.
        /// </summary>
        [HttpPost("api/invoice/{id:int}/approve")]
        [Authorize(Roles = "sales_executive,SalesExecutive,admin,Admin,super_admin,SuperAdmin")]
        [RecordScope(RecordKind.Invoice, "id")]
        public async Task<IActionResult> ApproveInvoice(int id)
        {
            if (id <= 0) return BadRequest(new { isSuccessful = false, message = "Invalid invoice ID." });

            // Super admins may view the queue only (403 on approve)
            if (User.IsSuperAdmin())
            {
                return StatusCode(403, new { isSuccessful = false, message = "Super admins may review the approval queue, but approvals must be performed by the assigned location sales executive or admin." });
            }

            if (!await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Invoice, id.ToString()))
                return StatusCode(403, new { isSuccessful = false, message = "Location mismatch. You cannot approve invoices outside your assigned location." });

            try
            {
                using var conn = await OpenConnectionAsync();
                var userId = await ResolveUserIdAsync(conn);
                if (userId == null)
                    return Unauthorized(new { isSuccessful = false, message = "User record not found." });

                await SetAuditContextAsync(conn);

                using var cmd = new SqlCommand("dbo.WN_Invoices_ApproveSingle", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@InvoiceId", id);
                cmd.Parameters.AddWithValue("@UserId", userId.Value);

                var rowsAffected = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                if (rowsAffected == 0)
                {
                    // Query specific failure reason
                    using var checkCmd = new SqlCommand("SELECT ApprovalStatus, ViewedAt, ViewedBy FROM dbo.WN_Invoices WITH (NOLOCK) WHERE Id = @Id", conn);
                    checkCmd.Parameters.AddWithValue("@Id", id);
                    using var r = await checkCmd.ExecuteReaderAsync();
                    if (!await r.ReadAsync())
                        return NotFound(new { isSuccessful = false, message = "Invoice not found." });

                    var status = r.IsDBNull(0) ? null : r.GetString(0);
                    var viewedAt = r.IsDBNull(1) ? (DateTime?)null : r.GetDateTime(1);
                    var viewedBy = r.IsDBNull(2) ? (int?)null : r.GetInt32(2);

                    if (status != "PendingApproval")
                        return BadRequest(new { isSuccessful = false, message = $"Invoice cannot be approved because its approval status is '{status ?? "Approved"}' (not PendingApproval)." });
                    if (!viewedAt.HasValue)
                        return BadRequest(new { isSuccessful = false, message = "Invoice cannot be approved because it has not been viewed yet. Please open and inspect the invoice details before approving." });
                    if (viewedBy != userId.Value)
                        return BadRequest(new { isSuccessful = false, message = "Invoice was inspected by a different user. You must personally open and inspect the invoice details before approving." });

                    return BadRequest(new { isSuccessful = false, message = "Invoice could not be approved." });
                }

                // Call existing send-email path for this invoice
                var sendResult = await SendInvoiceEmailCoreAsync(id, conn);

                return Ok(new
                {
                    isSuccessful = true,
                    message = sendResult.Success
                        ? "Invoice approved and email sent successfully."
                        : $"Invoice approved, but email delivery failed ({sendResult.Message}). Queued for background retry."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Super admin moves one invoice from Approved (or NULL = approved) back to Pending Approval, so it is reviewed again
        /// in the approval queue (dbo.WN_Invoices_RevertApproval, script INV_REVERT_01). Body: { "reason": "..." } (required,
        /// at most 500 characters). Refused (400) when the invoice is deleted / cancelled, already pending, on 1Bill/EzPay or
        /// has money against it (void the fee receipts first); 404 when there is no such invoice. The rules are checked here
        /// first and again by the procedure with the invoice row locked. The invoice's still-pending e-mail retries are
        /// cancelled (the customer already has the original e-mail; the next approval sends it again).
        /// </summary>
        [HttpPost("api/invoice/{id:int}/revert-approval")]
        [Authorize(Roles = SuperAdminRoles)]
        public async Task<IActionResult> RevertApproval(int id, [FromBody] RevertInvoiceApprovalRequest? req)
        {
            if (!User.IsSuperAdmin())
                return StatusCode(403, ApiResponse.Fail(InvoiceApprovalRevertRules.ForbiddenMessage));
            if (id <= 0)
                return BadRequest(ApiResponse.Fail("Invalid invoice ID."));

            var (reason, reasonError) = InvoiceApprovalRevertRules.ValidateReason(req?.Reason);
            if (reasonError != null)
                return BadRequest(ApiResponse.Fail(reasonError));

            try
            {
                // Validate before the procedure: the same rules it checks again under the lock (race safety).
                var state = await _db.GetInvoiceRevertStateDbAsync(id);
                if (state == null)
                    return NotFound(ApiResponse.Fail(InvoiceApprovalRevertRules.NotFoundMessage));
                var ruleError = InvoiceApprovalRevertRules.Check(state);
                if (ruleError != null)
                    return BadRequest(ApiResponse.Fail(ruleError));

                var email = ResolveUserEmail();
                var userId = await ResolveRevertUserIdAsync(email);
                if (userId == null)
                    return Unauthorized(ApiResponse.Fail("User record not found."));

                // The repository's connection sets the audit session context (AppUserId / AppUserEmail / SourceApp) for the
                // signed-in user exactly as SetAuditContextAsync does, on the connection that runs the procedure.
                var result = await _db.RevertInvoiceApprovalDbAsync(id, userId.Value, email, reason!);
                if (result == null)
                    return StatusCode(500, ApiResponse.Fail("The invoice could not be moved back to Pending Approval. Please try again."));

                return Ok(ApiResponse.Ok(new
                {
                    invoiceId = result.InvoiceId,
                    invoiceNumber = result.InvoiceNumber,
                    approvalStatus = InvoiceApprovalRevertRules.PendingApproval,
                    cancelledDeliveryRows = result.CancelledDeliveryRows
                }, InvoiceApprovalRevertRules.SuccessMessage));
            }
            catch (InvoiceApprovalRevertException ex)
            {
                return ex.IsNotFound
                    ? NotFound(ApiResponse.Fail(InvoiceApprovalRevertRules.NotFoundMessage))
                    : BadRequest(ApiResponse.Fail(ex.Message));
            }
            catch (InvoiceApprovalRevertDatabaseMissingException ex)
            {
                return StatusCode(503, ApiResponse.Fail(ex.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse.Fail(ex.Message));
            }
        }

        /// <summary>The signed-in user's id: the JWT id claim, else looked up by the JWT email (as ResolveUserIdAsync).</summary>
        private async Task<int?> ResolveRevertUserIdAsync(string? email)
        {
            var idClaim = User.FindFirst("id")?.Value ?? User.FindFirst("userId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(idClaim, out var parsedId) && parsedId > 0) return parsedId;
            if (string.IsNullOrWhiteSpace(email)) return null;
            var (id, _) = await _db.GetUserIdByEmailAsync(email);
            return id is > 0 ? id : null;
        }

        /// <summary>
        /// Whether the booking's first invoice exists yet (dbo.WN_GetInvoiceByBookingId, the same check the send below
        /// makes): the staff app shows "Send initial invoice" on a booking until it has been sent.
        /// </summary>
        [HttpGet("api/booking/{bookingId:int}/initial-invoice")]
        [Authorize(Roles = StaffRoles)]
        [RecordScope(RecordKind.Booking, "bookingId")]
        public async Task<IActionResult> GetInitialInvoiceStatus(int bookingId)
        {
            try
            {
                using var conn = await OpenConnectionAsync();
                using var cmd = new SqlCommand("dbo.WN_GetInvoiceByBookingId", conn) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@BookingId", bookingId);
                using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync())
                    return Ok(new { isSuccessful = true, data = new { bookingId, sent = false, invoiceId = (int?)null, invoiceNumber = (string?)null } });
                return Ok(new
                {
                    isSuccessful = true,
                    data = new
                    {
                        bookingId,
                        sent = true,
                        invoiceId = (int?)r.GetInt32(r.GetOrdinal("Id")),
                        invoiceNumber = r.IsDBNull(r.GetOrdinal("InvoiceNumber")) ? null : r.GetString(r.GetOrdinal("InvoiceNumber"))
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [EnableRateLimiting("pdf")]
        [HttpPost("api/invoice/send-initial/{bookingId:int}")]
        [Authorize(Roles = StaffRoles)]
        [HttpPost("api/booking/{bookingId:int}/send-initial-invoice")]
        [RecordScope(RecordKind.Booking, "bookingId")]
        public async Task<IActionResult> SendInitialInvoiceForBooking(int bookingId, [FromQuery] DateTime? issuedOn = null, [FromQuery] bool preview = false,
            [FromQuery] string? invoiceType = null, [FromQuery] decimal? whTaxId = null)
        {
            // invoiceType (optional, chosen on the Initial Invoice Preview): "standard" or "wht" (whTaxId = the WN_WHTaxRate Id).
            // Omitted: the booking's own WHT setting is used, as before.
            if (invoiceType is not null && invoiceType != "standard" && invoiceType != "wht")
                return BadRequest(new { isSuccessful = false, message = "invoiceType must be 'standard' or 'wht'." });
            if (invoiceType == "wht" && !await _db.IsActiveWhtTaxRateIdAsync(whTaxId))
                return BadRequest(new { isSuccessful = false, message = "Select a WHT rate from the list." });
            decimal? chosenWhTaxId = invoiceType == "wht" ? whTaxId : null;

            // Preview saves nothing, so it never takes the lock.
            if (preview) return await SendInitialInvoiceCoreAsync(bookingId, issuedOn, preview, invoiceType, chosenWhTaxId);

            // Check-then-insert below (WN_GetInvoiceByBookingId, then create) is not atomic: two clicks / concurrent
            // requests could both see no invoice and each create an invoice + ledger voucher. Serialise per booking
            // across all API instances with a session-owned SQL Server application lock held on a dedicated connection
            // for the whole operation. The second request waits, then finds the first request's invoice and takes the
            // existing re-send / update path instead of inserting a duplicate.
            var lockResource = $"WN_InitialInvoice_{bookingId}";
            await using var lockConn = new SqlConnection(GetConnectionString());
            await lockConn.OpenAsync();
            bool lockTaken = false;
            try
            {
                using (var getLock = new SqlCommand("sp_getapplock", lockConn))
                {
                    getLock.CommandType = CommandType.StoredProcedure;
                    getLock.CommandTimeout = 60;
                    getLock.Parameters.AddWithValue("@Resource", lockResource);
                    getLock.Parameters.AddWithValue("@LockMode", "Exclusive");
                    getLock.Parameters.AddWithValue("@LockOwner", "Session");
                    getLock.Parameters.AddWithValue("@LockTimeout", 30000);
                    var ret = getLock.Parameters.Add("@ReturnValue", SqlDbType.Int);
                    ret.Direction = ParameterDirection.ReturnValue;
                    await getLock.ExecuteNonQueryAsync();
                    var code = ret.Value is int c ? c : -999;
                    if (code < 0)
                        return Conflict(new { isSuccessful = false, message = "This booking's first invoice is already being created - try again in a moment." });
                    lockTaken = true;
                }

                return await SendInitialInvoiceCoreAsync(bookingId, issuedOn, preview, invoiceType, chosenWhTaxId);
            }
            finally
            {
                if (lockTaken)
                {
                    try
                    {
                        using var releaseLock = new SqlCommand("sp_releaseapplock", lockConn);
                        releaseLock.CommandType = CommandType.StoredProcedure;
                        releaseLock.Parameters.AddWithValue("@Resource", lockResource);
                        releaseLock.Parameters.AddWithValue("@LockOwner", "Session");
                        await releaseLock.ExecuteNonQueryAsync();
                    }
                    catch
                    {
                        // Drop the pooled physical connection so its session (and the lock it owns) really ends.
                        SqlConnection.ClearPool(lockConn);
                    }
                }
            }
        }

        private async Task<IActionResult> SendInitialInvoiceCoreAsync(int bookingId, DateTime? issuedOn, bool preview,
            string? invoiceType = null, decimal? chosenWhTaxId = null)
        {
            try
            {
                using var conn = await OpenConnectionAsync();

                int userId = 0;
                decimal monthlyRent = 0, subtotalAmount = 0, totalAmount = 0, discountAmount = 0, discountPercentage = 0, secDepositReq = 0, resolvedSecDep = 0;
                int billingMonths = 3, secMonths = 0, spaceCapacity = 1;   // deposit months: stored value only, no default
                decimal appliedTaxPercentage = 16.00m, appliedChargePercentage = 10.00m, perSeatSupportRate = 2000.00m;
                int? rentAccountId = null, securityReceivedId = null, servicesIncomeId = null, salesTaxId = null, accountReceivableId = null;
                string discountType = "Percentage";
                DateTime? startOn = null, endOn = null;
                string customerEmail = "", customerName = "", spaceName = "Workspace", spaceTypeName = "", categoryCode = "";

                using (var bCmd = new SqlCommand("dbo.WN_GetBookingInvoiceDetails", conn))
                {
                    bCmd.CommandType = CommandType.StoredProcedure;
                    bCmd.Parameters.AddWithValue("@BookingId", bookingId);
                    using var reader = await bCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        userId = reader.GetInt32(reader.GetOrdinal("UserId"));
                        startOn = reader.IsDBNull(reader.GetOrdinal("StartOn")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("StartOn"));
                        endOn = reader.IsDBNull(reader.GetOrdinal("EndOn")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("EndOn"));
                        monthlyRent = reader.IsDBNull(reader.GetOrdinal("MonthlyRent")) ? 0 : reader.GetDecimal(reader.GetOrdinal("MonthlyRent"));
                        subtotalAmount = reader.IsDBNull(reader.GetOrdinal("SubtotalAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("SubtotalAmount"));
                        totalAmount = reader.IsDBNull(reader.GetOrdinal("TotalAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("TotalAmount"));
                        discountAmount = reader.IsDBNull(reader.GetOrdinal("DiscountAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("DiscountAmount"));
                        discountPercentage = reader.IsDBNull(reader.GetOrdinal("DiscountPercentage")) ? 0 : reader.GetDecimal(reader.GetOrdinal("DiscountPercentage"));
                        discountType = reader.IsDBNull(reader.GetOrdinal("DiscountType")) ? "Percentage" : reader.GetString(reader.GetOrdinal("DiscountType"));
                        secDepositReq = reader.IsDBNull(reader.GetOrdinal("SecurityDepositRequired")) ? 0 : reader.GetDecimal(reader.GetOrdinal("SecurityDepositRequired"));
                        resolvedSecDep = HasColumn(reader, "ResolvedSecurityDeposit") && !reader.IsDBNull(reader.GetOrdinal("ResolvedSecurityDeposit")) ? reader.GetDecimal(reader.GetOrdinal("ResolvedSecurityDeposit")) : 0;
                        billingMonths = reader.IsDBNull(reader.GetOrdinal("BillingPeriodMonths")) ? 3 : reader.GetInt32(reader.GetOrdinal("BillingPeriodMonths"));
                        secMonths = reader.IsDBNull(reader.GetOrdinal("SecurityDepositMonths")) ? 0 : reader.GetInt32(reader.GetOrdinal("SecurityDepositMonths"));
                        customerEmail = reader.IsDBNull(reader.GetOrdinal("CustomerEmail")) ? "" : reader.GetString(reader.GetOrdinal("CustomerEmail"));
                        customerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? "Valued Customer" : reader.GetString(reader.GetOrdinal("CustomerName"));
                        spaceName = reader.IsDBNull(reader.GetOrdinal("SpaceName")) ? "Workspace" : reader.GetString(reader.GetOrdinal("SpaceName"));
                        spaceTypeName = reader.IsDBNull(reader.GetOrdinal("SpaceTypeName")) ? "" : reader.GetString(reader.GetOrdinal("SpaceTypeName"));
                        categoryCode = reader.IsDBNull(reader.GetOrdinal("CategoryCode")) ? "" : reader.GetString(reader.GetOrdinal("CategoryCode"));
                        spaceCapacity = HasColumn(reader, "SpaceCapacity") && !reader.IsDBNull(reader.GetOrdinal("SpaceCapacity")) ? Convert.ToInt32(reader["SpaceCapacity"]) : 1;
                        appliedTaxPercentage = HasColumn(reader, "AppliedTaxPercentage") && !reader.IsDBNull(reader.GetOrdinal("AppliedTaxPercentage")) ? Convert.ToDecimal(reader["AppliedTaxPercentage"]) : 16.00m;
                        appliedChargePercentage = HasColumn(reader, "AppliedChargePercentage") && !reader.IsDBNull(reader.GetOrdinal("AppliedChargePercentage")) ? Convert.ToDecimal(reader["AppliedChargePercentage"]) : 10.00m;
                        perSeatSupportRate = HasColumn(reader, "PerSeatSupportRate") && !reader.IsDBNull(reader.GetOrdinal("PerSeatSupportRate")) ? Convert.ToDecimal(reader["PerSeatSupportRate"]) : 2000.00m;
                        rentAccountId = HasColumn(reader, "RentAccountId") && !reader.IsDBNull(reader.GetOrdinal("RentAccountId")) ? (int?)Convert.ToInt32(reader["RentAccountId"]) : null;
                        securityReceivedId = HasColumn(reader, "SecurityReceivedId") && !reader.IsDBNull(reader.GetOrdinal("SecurityReceivedId")) ? (int?)Convert.ToInt32(reader["SecurityReceivedId"]) : null;
                        servicesIncomeId = HasColumn(reader, "ServicesIncomeId") && !reader.IsDBNull(reader.GetOrdinal("ServicesIncomeId")) ? (int?)Convert.ToInt32(reader["ServicesIncomeId"]) : null;
                        salesTaxId = HasColumn(reader, "SalesTaxId") && !reader.IsDBNull(reader.GetOrdinal("SalesTaxId")) ? (int?)Convert.ToInt32(reader["SalesTaxId"]) : null;
                        accountReceivableId = HasColumn(reader, "AccountReceivableId") && !reader.IsDBNull(reader.GetOrdinal("AccountReceivableId")) ? (int?)Convert.ToInt32(reader["AccountReceivableId"]) : null;
                    }
                    else
                    {
                        return NotFound(new { isSuccessful = false, message = "Booking not found." });
                    }
                }

                int contractMonths = (startOn.HasValue && endOn.HasValue && (endOn.Value - startOn.Value).TotalDays > 20) 
                    ? Math.Max(1, (int)Math.Round((endOn.Value - startOn.Value).TotalDays / 30.4375)) 
                    : 12;

                var calcReq = new WorkNest.Application.Services.InvoiceCalculationRequest
                {
                    SpaceTypeName = spaceTypeName,
                    CategoryCode = categoryCode,
                    Capacity = spaceCapacity,
                    MonthlyRent = monthlyRent,
                    SubtotalAmount = subtotalAmount,
                    CurrentCycleAmount = subtotalAmount > 0 ? subtotalAmount : totalAmount,
                    BillingPeriodMonths = billingMonths,
                    ContractPeriodMonths = contractMonths,
                    // Stored deposit (SecurityDepositRules); a stored 0 with months above 0 on a private office is worked
                    // out from the months; 0 months = no deposit and no deposit line.
                    SecurityDeposit = secDepositReq > 0 ? secDepositReq : resolvedSecDep,
                    SecurityDepositMonths = secMonths,
                    PerSeatSupportRate = perSeatSupportRate,
                    AppliedChargePercentage = appliedChargePercentage,
                    AppliedTaxPercentage = appliedTaxPercentage,
                    DiscountType = discountType,
                    DiscountPercentage = discountPercentage,
                    DiscountAmount = discountAmount,
                    StartOn = startOn,
                    EndOn = endOn
                };

                var calc = WorkNest.Application.Services.InvoiceCalculationEngine.CalculateInvoice(calcReq, _clock.Today);

                DateTime periodStart = calc.BillingPeriodStart;
                DateTime periodEnd = calc.BillingPeriodEnd;
                decimal grossAdvanceRent = calc.Rent;
                decimal appliedDiscount = calc.Discount;
                decimal taxTotal = calc.Tax;
                decimal supportCharge = calc.ServiceCharge;
                decimal securityDeposit = calc.DepositFirstInstallment;
                if (securityDeposit <= 0) secMonths = 0;   // no deposit: no months on the invoice either
                decimal roomRentExclusive = calc.RoomRentExclTax;
                decimal expectedSubtotal = calc.Rent;
                decimal expectedGrandTotal = calc.GrandTotal;

                string mainLineDescription;
                if (calc.IsMeetingRoom)
                {
                    mainLineDescription = $"Meeting Room Booking Rent ({spaceName})";
                }
                else if (calc.IsProrated)
                {
                    int fullMonths = (startOn.HasValue && startOn.Value.Day < 15) ? Math.Max(0, calc.BillingPeriodMonths - 1) : calc.BillingPeriodMonths;
                    mainLineDescription = $"Private Office Charges for {periodStart:MMM d, yyyy} to {periodEnd:MMM d, yyyy} ({calc.ProratedDays} days prorated + {fullMonths} Month(s))";
                }
                else
                {
                    mainLineDescription = $"Private Office Charges for {periodStart:MMM d, yyyy} to {periodEnd:MMM d, yyyy}";
                }

                string secDepositDescription = secMonths > 0 
                    ? $"Security Deposit ({secMonths} Month(s) Refundable - {spaceName})" 
                    : $"Security Deposit (Refundable - {spaceName})";
                decimal secDepositUnitPrice = (secMonths > 0 && securityDeposit > 0)
                    ? Math.Round(securityDeposit / secMonths, 2)
                    : securityDeposit;
                decimal secDepositQty = (secMonths > 0 && securityDeposit > 0) ? secMonths : 1m;

                // Preview: return exactly what this invoice will contain (same calculation), without saving
                // or emailing anything - the admin preview window shows these figures.
                if (preview)
                {
                    // Invoice date is always today; due 7 days later.
                    var invoiceDate = _clock.Today;

                    // WHT: the booking's current choice (preselects the preview's dropdowns), and the choice previewed.
                    var (bookingSendsWht, bookingWhTaxId) = await _db.GetBookingWhtAsync(bookingId);
                    decimal? previewWhTaxId = invoiceType is null ? bookingWhTaxId : chosenWhTaxId;
                    decimal whtRatePercent = 0m, whtAmount = 0m, whtGrossUp = 0m, previewGrandTotal = expectedGrandTotal;
                    if (previewWhTaxId is > 0)
                    {
                        whtRatePercent = await _db.ResolveWhtRatePercentAsync(previewWhTaxId);
                        if (whtRatePercent > 0m && whtRatePercent < 100m)
                        {
                            // Same inputs WN_Invoices_Insert grosses up (step 11 rounding absorbed into the rent first).
                            decimal rentForWht = roomRentExclusive > 0
                                ? expectedGrandTotal - supportCharge - taxTotal - securityDeposit
                                : roomRentExclusive;
                            var wht = WorkNest.Application.Services.WhtCalculator.Calculate(rentForWht, supportCharge, taxTotal, whtRatePercent);
                            whtAmount = wht.WhtAmount;
                            whtGrossUp = wht.RentLine - rentForWht;
                            previewGrandTotal = wht.RentLine + wht.SupportLine + wht.TaxLine + securityDeposit;
                        }
                    }
                    bool isWhtPreview = whtAmount > 0m;

                    // An existing first invoice is only re-sent as posted: the preview's WHT choice cannot change it.
                    bool hasExistingInvoice = false;
                    using (var exCmd = new SqlCommand("dbo.WN_GetInvoiceByBookingId", conn))
                    {
                        exCmd.CommandType = CommandType.StoredProcedure;
                        exCmd.Parameters.AddWithValue("@BookingId", bookingId);
                        using var exR = await exCmd.ExecuteReaderAsync();
                        hasExistingInvoice = await exR.ReadAsync();
                    }
                    return Ok(new
                    {
                        isSuccessful = true,
                        data = new
                        {
                            bookingId,
                            issuedOn = invoiceDate.ToString("yyyy-MM-dd"),
                            dueOn = invoiceDate.AddDays(InvoiceDueDays).ToString("yyyy-MM-dd"),
                            periodStart = periodStart.ToString("yyyy-MM-dd"),
                            periodEnd = periodEnd.ToString("yyyy-MM-dd"),
                            description = mainLineDescription,
                            monthlyRent = calc.MonthlyRent,
                            billingMonths = calc.BillingPeriodMonths,
                            isProrated = calc.IsProrated,
                            proratedDays = calc.ProratedDays,
                            rent = grossAdvanceRent,
                            discount = appliedDiscount,
                            discountPercentage = grossAdvanceRent > 0 ? Math.Round(appliedDiscount * 100m / grossAdvanceRent, 2) : 0m,
                            tax = taxTotal,
                            serviceCharge = supportCharge,
                            securityDeposit,
                            securityDepositMonths = secMonths,
                            grandTotal = previewGrandTotal,
                            hasExistingInvoice,
                            bookingWhTaxId = bookingSendsWht ? bookingWhTaxId : null,
                            isWhtInvoice = isWhtPreview,
                            whTaxId = isWhtPreview ? previewWhTaxId : null,
                            whtRatePercent = isWhtPreview ? whtRatePercent : 0m,
                            whtGrossUp,
                            whtAmount,
                            netPayableAfterWht = previewGrandTotal - whtAmount
                        }
                    });
                }

                int existingId = 0;
                using (var checkCmd = new SqlCommand("dbo.WN_GetInvoiceByBookingId", conn))
                {
                    checkCmd.CommandType = CommandType.StoredProcedure;
                    checkCmd.Parameters.AddWithValue("@BookingId", bookingId);
                    using var r = await checkCmd.ExecuteReaderAsync();
                    if (await r.ReadAsync())
                    {
                        existingId = r.GetInt32(r.GetOrdinal("Id"));
                    }
                }

                // An existing first invoice (not cancelled) is sent again exactly as it was posted. Rewriting its
                // amounts here left its ledger voucher and sales tax invoice on the old figures; to change an
                // unpaid invoice, cancel it and send the first invoice again (a new invoice is created).
                if (existingId > 0)
                {
                    return await SendInvoiceEmail(existingId);
                }

                // WHT chosen on the Initial Invoice Preview: saved on the booking (the WN_WHTaxRate Id, not the rate)
                // before the invoice is created, so WN_Invoices_Insert grosses it up and issues InvoiceTypeId 6.
                // Standard clears it. Omitted: the booking keeps its own setting.
                if (invoiceType is not null)
                    await _db.SetBookingWhtAsync(bookingId, chosenWhTaxId);

                var dto = new CreateCustomInvoiceDto
                {
                    BookingId = bookingId,
                    UserId = userId,
                    IssuedOn = _clock.Today,                              // invoice date: today
                    DueOn = _clock.Today.AddDays(InvoiceDueDays),         // due: invoice date + 7 days
                    Notes = $"Initial Payment Invoice for Booking #{bookingId} - {spaceName}",
                    SendEmail = true,
                    BillingPeriodStart = periodStart,
                    BillingPeriodEnd = periodEnd,
                    AdvanceRentMonths = calc.BillingPeriodMonths,
                    SecurityDepositMonths = secMonths,
                    SecurityDepositAmount = securityDeposit,
                    RoomRentExclTax = roomRentExclusive,
                    ServiceCharges = supportCharge,
                    TaxOnServiceCharges = taxTotal,
                    SubTotal = expectedSubtotal,
                    DiscountTotal = appliedDiscount,
                    TaxTotal = taxTotal,
                    GrandTotal = expectedGrandTotal,
                    RentAccountId = rentAccountId,
                    SecurityReceivedId = securityReceivedId,
                    ServicesIncomeId = servicesIncomeId,
                    SalesTaxId = salesTaxId,
                    AccountReceivableId = accountReceivableId,
                    AccountsCoaId = rentAccountId,
                    Lines = new List<CreateCustomInvoiceLineDto>()
                };

                if (calc.NetRent > 0)
                {
                    dto.Lines.Add(new CreateCustomInvoiceLineDto
                    {
                        Description = mainLineDescription,
                        Quantity = 1,
                        UnitPrice = calc.NetRent,
                        DiscountAmount = 0m,
                        TaxRate = 0m,
                        ChargeTypeId = 1
                    });
                }

                if (securityDeposit > 0)
                {
                    dto.Lines.Add(new CreateCustomInvoiceLineDto
                    {
                        Description = secDepositDescription,
                        Quantity = secDepositQty,
                        UnitPrice = secDepositUnitPrice,
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
                        TaxRate = 0m,
                        ChargeTypeId = 1
                    });
                }

                return await CreateCustomInvoice(dto);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        /// <summary>
        /// On-demand generation and optional delivery of a recurring invoice for an active booking.
        /// Invokes dbo.WN_Invoice_CreateRecurring with duplicate detection, automatic next-period resolution,
        /// and optional immediate approval and email delivery.
        /// </summary>
        [HttpPost("api/booking/{bookingId:int}/generate-recurring-invoice")]
        [Authorize(Roles = StaffRoles)]
        [RecordScope(RecordKind.Booking, "bookingId")]
        public async Task<IActionResult> GenerateRecurringInvoiceForBooking(
            int bookingId,
            [FromBody] GenerateRecurringInvoiceRequest? request = null)
        {
            if (bookingId <= 0) return BadRequest(new { isSuccessful = false, message = "Invalid booking ID." });

            var lockResource = $"WN_RecurringInvoice_{bookingId}";
            await using var lockConn = new SqlConnection(GetConnectionString());
            await lockConn.OpenAsync();
            bool lockTaken = false;
            try
            {
                using (var getLock = new SqlCommand("sp_getapplock", lockConn))
                {
                    getLock.CommandType = CommandType.StoredProcedure;
                    getLock.CommandTimeout = 60;
                    getLock.Parameters.AddWithValue("@Resource", lockResource);
                    getLock.Parameters.AddWithValue("@LockMode", "Exclusive");
                    getLock.Parameters.AddWithValue("@LockOwner", "Session");
                    getLock.Parameters.AddWithValue("@LockTimeout", 30000);
                    var ret = getLock.Parameters.Add("@ReturnValue", SqlDbType.Int);
                    ret.Direction = ParameterDirection.ReturnValue;
                    await getLock.ExecuteNonQueryAsync();
                    var code = ret.Value is int c ? c : -999;
                    if (code < 0)
                        return Conflict(new { isSuccessful = false, message = "A recurring invoice for this booking is already being processed - try again in a moment." });
                    lockTaken = true;
                }

                using var conn = await OpenConnectionAsync();
                await SetAuditContextAsync(conn);

                // 1. Fetch booking info
                DateTime contractStart, contractEnd;
                int bookingStatusId;
                using (var bCmd = new SqlCommand("SELECT StartOn, EndOn, BookingStatusId FROM dbo.WN_Bookings WITH (NOLOCK) WHERE Id = @BookingId", conn))
                {
                    bCmd.Parameters.AddWithValue("@BookingId", bookingId);
                    using var r = await bCmd.ExecuteReaderAsync();
                    if (!await r.ReadAsync())
                        return NotFound(new { isSuccessful = false, message = $"Booking #{bookingId} not found." });

                    contractStart = r.IsDBNull(0) ? _clock.Today : r.GetDateTime(0);
                    contractEnd = r.IsDBNull(1) ? contractStart.AddYears(1) : r.GetDateTime(1);
                    bookingStatusId = Convert.ToInt32(r.GetValue(2));
                }

                if (!new[] { 1, 2, 5, 33 }.Contains(bookingStatusId))
                {
                    return BadRequest(new { isSuccessful = false, message = $"Booking #{bookingId} is not in an active status (Current status ID: {bookingStatusId})." });
                }

                // 2. Determine Period Start & End
                DateTime periodStart;
                if (request?.BillingPeriodStart.HasValue == true)
                {
                    periodStart = request.BillingPeriodStart.Value.Date;
                }
                else
                {
                    using var lastEndCmd = new SqlCommand("dbo.WN_GetBookingLastInvoicePeriodEnd", conn);
                    lastEndCmd.CommandType = CommandType.StoredProcedure;
                    lastEndCmd.Parameters.AddWithValue("@BookingId", bookingId);
                    var lastEndVal = await lastEndCmd.ExecuteScalarAsync();
                    if (lastEndVal != null && lastEndVal != DBNull.Value)
                    {
                        periodStart = ((DateTime)lastEndVal).Date.AddDays(1);
                    }
                    else
                    {
                        periodStart = contractStart.Date;
                    }
                }

                if (periodStart >= contractEnd.Date)
                {
                    return BadRequest(new { isSuccessful = false, message = $"The contract ended on {contractEnd:MMM d, yyyy}; there is no further recurring period to invoice." });
                }

                DateTime periodEnd;
                if (request?.BillingPeriodEnd.HasValue == true)
                {
                    periodEnd = request.BillingPeriodEnd.Value.Date;
                }
                else
                {
                    DateTime rawEnd = WorkNest.Application.Services.BillingPeriods.PeriodEnd(contractStart, periodStart, 1);
                    periodEnd = rawEnd > contractEnd.Date ? contractEnd.Date : rawEnd;
                }

                // 3. Duplicate check
                using (var checkCmd = new SqlCommand("dbo.WN_Invoice_CheckExistingBillingPeriod", conn))
                {
                    checkCmd.CommandType = CommandType.StoredProcedure;
                    checkCmd.Parameters.AddWithValue("@BookingId", bookingId);
                    checkCmd.Parameters.AddWithValue("@BillingPeriodStart", periodStart);
                    checkCmd.Parameters.AddWithValue("@BillingPeriodEnd", periodEnd);
                    var res = await checkCmd.ExecuteScalarAsync();
                    if (Convert.ToInt32(res) == 1)
                    {
                        using var exCmd = new SqlCommand(@"
                            SELECT TOP 1 Id, InvoiceNumber, GrandTotal, ApprovalStatus
                            FROM dbo.WN_Invoices WITH (NOLOCK)
                            WHERE BookingId = @BookingId 
                              AND BillingPeriodStart = @BillingPeriodStart 
                              AND BillingPeriodEnd = @BillingPeriodEnd
                              AND StatusId NOT IN (5, 40)
                            ORDER BY Id DESC", conn);
                        exCmd.Parameters.AddWithValue("@BookingId", bookingId);
                        exCmd.Parameters.AddWithValue("@BillingPeriodStart", periodStart);
                        exCmd.Parameters.AddWithValue("@BillingPeriodEnd", periodEnd);

                        int existingId = 0;
                        string existingNo = "";
                        decimal existingTotal = 0;
                        string existingStatus = "";
                        using var exReader = await exCmd.ExecuteReaderAsync();
                        if (await exReader.ReadAsync())
                        {
                            existingId = exReader.GetInt32(0);
                            existingNo = exReader.GetString(1);
                            existingTotal = exReader.GetDecimal(2);
                            existingStatus = exReader.IsDBNull(3) ? "Approved" : exReader.GetString(3);
                        }

                        if (request?.SendIfAlreadyExists == true && existingId > 0)
                        {
                            var sendRes = await SendInvoiceEmailCoreAsync(existingId, conn);
                            return Ok(new
                            {
                                isSuccessful = true,
                                alreadyExisted = true,
                                invoiceId = existingId,
                                invoiceNumber = existingNo,
                                totalAmount = existingTotal,
                                billingPeriodStart = periodStart,
                                billingPeriodEnd = periodEnd,
                                approvalStatus = existingStatus,
                                emailSent = sendRes.Success,
                                message = $"Invoice {existingNo} already exists for period [{periodStart:yyyy-MM-dd} to {periodEnd:yyyy-MM-dd}]. Email delivery result: {sendRes.Message}"
                            });
                        }

                        return Ok(new
                        {
                            isSuccessful = true,
                            alreadyExisted = true,
                            invoiceId = existingId,
                            invoiceNumber = existingNo,
                            totalAmount = existingTotal,
                            billingPeriodStart = periodStart,
                            billingPeriodEnd = periodEnd,
                            approvalStatus = existingStatus,
                            emailSent = false,
                            message = $"Invoice {existingNo} already exists for period [{periodStart:yyyy-MM-dd} to {periodEnd:yyyy-MM-dd}]. Pass sendIfAlreadyExists=true to resend."
                        });
                    }
                }

                // 4. Create Recurring Invoice via dbo.WN_Invoice_CreateRecurring
                int createdInvoiceId;
                string createdInvoiceNumber;
                decimal createdTotal;
                using (var genCmd = new SqlCommand("dbo.WN_Invoice_CreateRecurring", conn))
                {
                    genCmd.CommandType = CommandType.StoredProcedure;
                    genCmd.Parameters.AddWithValue("@BookingId", bookingId);
                    genCmd.Parameters.AddWithValue("@BillingPeriodStart", periodStart);
                    genCmd.Parameters.AddWithValue("@BillingPeriodEnd", periodEnd);

                    using var genReader = await genCmd.ExecuteReaderAsync();
                    int invIdOrd = -1, numOrd = -1, totOrd = -1;
                    bool readOk = false;
                    do
                    {
                        if (await genReader.ReadAsync())
                        {
                            try { invIdOrd = genReader.GetOrdinal("InvoiceId"); } catch { }
                            try { numOrd = genReader.GetOrdinal("InvoiceNumber"); } catch { }
                            try { totOrd = genReader.GetOrdinal("TotalAmount"); } catch { }

                            if (invIdOrd >= 0 && !genReader.IsDBNull(invIdOrd))
                            {
                                readOk = true;
                                if (totOrd >= 0) break;
                            }
                        }
                    } while (await genReader.NextResultAsync());

                    if (!readOk || invIdOrd < 0 || genReader.IsDBNull(invIdOrd))
                    {
                        return StatusCode(500, new { isSuccessful = false, message = "Recurring invoice generation returned null. Check booking details and active status." });
                    }

                    createdInvoiceId = genReader.GetInt32(invIdOrd);
                    createdInvoiceNumber = numOrd >= 0 && !genReader.IsDBNull(numOrd) ? genReader.GetString(numOrd) : "";
                    createdTotal = totOrd >= 0 && !genReader.IsDBNull(totOrd) ? genReader.GetDecimal(totOrd) : 0m;
                }

                // 5. Handle Auto-Approve and Send if requested
                string approvalStatus = "PendingApproval";
                bool emailSent = false;
                string emailMessage = "";

                if (request?.AutoApproveAndSend == true)
                {
                    var currentUserId = await ResolveUserIdAsync(conn);
                    using (var appCmd = new SqlCommand(@"
                        UPDATE dbo.WN_Invoices 
                        SET ApprovalStatus = 'Approved', 
                            ApprovedAt = SYSUTCDATETIME(), 
                            ApprovedBy = @UserId,
                            UpdatedOn = SYSUTCDATETIME()
                        WHERE Id = @Id", conn))
                    {
                        appCmd.Parameters.AddWithValue("@Id", createdInvoiceId);
                        appCmd.Parameters.AddWithValue("@UserId", (object?)currentUserId ?? DBNull.Value);
                        await appCmd.ExecuteNonQueryAsync();
                    }
                    approvalStatus = "Approved";

                    var sendRes = await SendInvoiceEmailCoreAsync(createdInvoiceId, conn);
                    emailSent = sendRes.Success;
                    emailMessage = sendRes.Message;
                }

                return Ok(new
                {
                    isSuccessful = true,
                    alreadyExisted = false,
                    invoiceId = createdInvoiceId,
                    invoiceNumber = createdInvoiceNumber,
                    totalAmount = createdTotal,
                    billingPeriodStart = periodStart,
                    billingPeriodEnd = periodEnd,
                    approvalStatus,
                    emailSent,
                    emailMessage = string.IsNullOrWhiteSpace(emailMessage) ? null : emailMessage,
                    message = emailSent
                        ? $"Recurring invoice {createdInvoiceNumber} generated, approved, and sent successfully."
                        : (request?.AutoApproveAndSend == true
                            ? $"Recurring invoice {createdInvoiceNumber} generated and approved, but email delivery failed ({emailMessage}). Queued for retry."
                            : $"Recurring invoice {createdInvoiceNumber} generated successfully and placed in PendingApproval queue.")
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
            finally
            {
                if (lockTaken)
                {
                    try
                    {
                        using var releaseLock = new SqlCommand("sp_releaseapplock", lockConn);
                        releaseLock.CommandType = CommandType.StoredProcedure;
                        releaseLock.Parameters.AddWithValue("@Resource", lockResource);
                        releaseLock.Parameters.AddWithValue("@LockOwner", "Session");
                        await releaseLock.ExecuteNonQueryAsync();
                    }
                    catch
                    {
                        SqlConnection.ClearPool(lockConn);
                    }
                }
            }
        }

        [HttpPost("api/booking/{bookingId:int}/generate-next-invoice")]
        [Authorize(Roles = StaffRoles)]
        [RecordScope(RecordKind.Booking, "bookingId")]
        public async Task<IActionResult> GenerateNextInvoiceForBooking(int bookingId)
        {
            // Read-the-last-period-then-insert is not atomic: a double click or two staff members could both bill the
            // same period (two invoices, two vouchers). Serialise per booking with a session-owned application lock,
            // same as the first invoice; the second request then sees the first one's period and bills the next.
            var lockResource = $"WN_NextInvoice_{bookingId}";
            await using var lockConn = new SqlConnection(GetConnectionString());
            await lockConn.OpenAsync();
            bool lockTaken = false;
            try
            {
                using (var getLock = new SqlCommand("sp_getapplock", lockConn))
                {
                    getLock.CommandType = CommandType.StoredProcedure;
                    getLock.CommandTimeout = 60;
                    getLock.Parameters.AddWithValue("@Resource", lockResource);
                    getLock.Parameters.AddWithValue("@LockMode", "Exclusive");
                    getLock.Parameters.AddWithValue("@LockOwner", "Session");
                    getLock.Parameters.AddWithValue("@LockTimeout", 30000);
                    var ret = getLock.Parameters.Add("@ReturnValue", SqlDbType.Int);
                    ret.Direction = ParameterDirection.ReturnValue;
                    await getLock.ExecuteNonQueryAsync();
                    var code = ret.Value is int c ? c : -999;
                    if (code < 0)
                        return Conflict(new { isSuccessful = false, message = "The next invoice for this booking is already being created - try again in a moment." });
                    lockTaken = true;
                }

                return await GenerateNextInvoiceCoreAsync(bookingId);
            }
            finally
            {
                if (lockTaken)
                {
                    try
                    {
                        using var releaseLock = new SqlCommand("sp_releaseapplock", lockConn);
                        releaseLock.CommandType = CommandType.StoredProcedure;
                        releaseLock.Parameters.AddWithValue("@Resource", lockResource);
                        releaseLock.Parameters.AddWithValue("@LockOwner", "Session");
                        await releaseLock.ExecuteNonQueryAsync();
                    }
                    catch
                    {
                        // Drop the pooled physical connection so its session (and the lock it owns) really ends.
                        SqlConnection.ClearPool(lockConn);
                    }
                }
            }
        }

        private async Task<IActionResult> GenerateNextInvoiceCoreAsync(int bookingId)
        {
            try
            {
                using var conn = await OpenConnectionAsync();

                int userId = 0;
                decimal monthlyRent = 0, subtotalAmount = 0, discountAmount = 0, discountPercentage = 0, totalAmount = 0;
                int billingMonths = 3, secMonths = 0, spaceCapacity = 1;   // deposit months: stored value only
                decimal appliedTaxPercentage = 16.00m, appliedChargePercentage = 10.00m, perSeatSupportRate = 2000.00m;
                int? rentAccountId = null, securityReceivedId = null, servicesIncomeId = null, salesTaxId = null, accountReceivableId = null;
                DateTime? startOn = null, endOn = null;
                string customerEmail = "", customerName = "", spaceName = "Workspace", spaceTypeName = "", categoryCode = "";

                using (var bCmd = new SqlCommand("dbo.WN_GetBookingInvoiceDetails", conn))
                {
                    bCmd.CommandType = CommandType.StoredProcedure;
                    bCmd.Parameters.AddWithValue("@BookingId", bookingId);
                    using var reader = await bCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        userId = reader.GetInt32(reader.GetOrdinal("UserId"));
                        startOn = reader.IsDBNull(reader.GetOrdinal("StartOn")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("StartOn"));
                        endOn = reader.IsDBNull(reader.GetOrdinal("EndOn")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("EndOn"));
                        monthlyRent = reader.IsDBNull(reader.GetOrdinal("MonthlyRent")) ? 0 : reader.GetDecimal(reader.GetOrdinal("MonthlyRent"));
                        subtotalAmount = reader.IsDBNull(reader.GetOrdinal("SubtotalAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("SubtotalAmount"));
                        totalAmount = reader.IsDBNull(reader.GetOrdinal("TotalAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("TotalAmount"));
                        discountAmount = reader.IsDBNull(reader.GetOrdinal("DiscountAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("DiscountAmount"));
                        discountPercentage = reader.IsDBNull(reader.GetOrdinal("DiscountPercentage")) ? 0 : reader.GetDecimal(reader.GetOrdinal("DiscountPercentage"));
                        billingMonths = reader.IsDBNull(reader.GetOrdinal("BillingPeriodMonths")) ? 3 : reader.GetInt32(reader.GetOrdinal("BillingPeriodMonths"));
                        secMonths = HasColumn(reader, "SecurityDepositMonths") && !reader.IsDBNull(reader.GetOrdinal("SecurityDepositMonths")) ? Convert.ToInt32(reader["SecurityDepositMonths"]) : 0;
                        customerEmail = reader.IsDBNull(reader.GetOrdinal("CustomerEmail")) ? "" : reader.GetString(reader.GetOrdinal("CustomerEmail"));
                        customerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? "Valued Customer" : reader.GetString(reader.GetOrdinal("CustomerName"));
                        spaceName = reader.IsDBNull(reader.GetOrdinal("SpaceName")) ? "Workspace" : reader.GetString(reader.GetOrdinal("SpaceName"));
                        spaceTypeName = reader.IsDBNull(reader.GetOrdinal("SpaceTypeName")) ? "" : reader.GetString(reader.GetOrdinal("SpaceTypeName"));
                        categoryCode = reader.IsDBNull(reader.GetOrdinal("CategoryCode")) ? "" : reader.GetString(reader.GetOrdinal("CategoryCode"));
                        spaceCapacity = HasColumn(reader, "SpaceCapacity") && !reader.IsDBNull(reader.GetOrdinal("SpaceCapacity")) ? Convert.ToInt32(reader["SpaceCapacity"]) : 1;
                        appliedTaxPercentage = HasColumn(reader, "AppliedTaxPercentage") && !reader.IsDBNull(reader.GetOrdinal("AppliedTaxPercentage")) ? Convert.ToDecimal(reader["AppliedTaxPercentage"]) : 16.00m;
                        appliedChargePercentage = HasColumn(reader, "AppliedChargePercentage") && !reader.IsDBNull(reader.GetOrdinal("AppliedChargePercentage")) ? Convert.ToDecimal(reader["AppliedChargePercentage"]) : 10.00m;
                        perSeatSupportRate = HasColumn(reader, "PerSeatSupportRate") && !reader.IsDBNull(reader.GetOrdinal("PerSeatSupportRate")) ? Convert.ToDecimal(reader["PerSeatSupportRate"]) : 2000.00m;
                        rentAccountId = HasColumn(reader, "RentAccountId") && !reader.IsDBNull(reader.GetOrdinal("RentAccountId")) ? (int?)Convert.ToInt32(reader["RentAccountId"]) : null;
                        securityReceivedId = HasColumn(reader, "SecurityReceivedId") && !reader.IsDBNull(reader.GetOrdinal("SecurityReceivedId")) ? (int?)Convert.ToInt32(reader["SecurityReceivedId"]) : null;
                        servicesIncomeId = HasColumn(reader, "ServicesIncomeId") && !reader.IsDBNull(reader.GetOrdinal("ServicesIncomeId")) ? (int?)Convert.ToInt32(reader["ServicesIncomeId"]) : null;
                        salesTaxId = HasColumn(reader, "SalesTaxId") && !reader.IsDBNull(reader.GetOrdinal("SalesTaxId")) ? (int?)Convert.ToInt32(reader["SalesTaxId"]) : null;
                        accountReceivableId = HasColumn(reader, "AccountReceivableId") && !reader.IsDBNull(reader.GetOrdinal("AccountReceivableId")) ? (int?)Convert.ToInt32(reader["AccountReceivableId"]) : null;
                    }
                    else
                    {
                        return NotFound(new { isSuccessful = false, message = "Booking not found." });
                    }
                }

                bool isMeetingRoom = spaceTypeName.Contains("Meeting", StringComparison.OrdinalIgnoreCase) ||
                                     spaceTypeName.Contains("Conference", StringComparison.OrdinalIgnoreCase) ||
                                     categoryCode.Contains("Meeting", StringComparison.OrdinalIgnoreCase) ||
                                     spaceName.Contains("Meeting", StringComparison.OrdinalIgnoreCase) ||
                                     spaceName.Contains("Conference", StringComparison.OrdinalIgnoreCase) ||
                                     (billingMonths <= 0 && startOn.HasValue && endOn.HasValue && (endOn.Value - startOn.Value).TotalDays < 20);

                DateTime periodStart = _clock.Today;
                using (var lastEndCmd = new SqlCommand("dbo.WN_GetBookingLastInvoicePeriodEnd", conn))
                {
                    lastEndCmd.CommandType = CommandType.StoredProcedure;
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

                // All subsequent recurring cycles after initial advance rent are billed monthly (1 month at a time).
                int recurringMonths = 1;
                // Anchored to the contract start so month-end starts don't drift (see BillingPeriods).
                DateTime periodEnd = WorkNest.Application.Services.BillingPeriods.PeriodEnd(startOn, periodStart, recurringMonths);

                // Never bill past the contract: stop once it has ended, and cut the last period at the contract end
                // (same cap as the billing job), charging only the months/days actually in that period.
                if (endOn.HasValue && periodStart.Date >= endOn.Value.Date)
                    return BadRequest(new { isSuccessful = false, message = $"The contract ended on {endOn.Value:MMM d, yyyy}; there is no further period to invoice." });
                decimal cycleMonths = recurringMonths;   // a normal recurring period is always 1 month
                if (endOn.HasValue && periodEnd.Date > endOn.Value.Date)
                {
                    periodEnd = endOn.Value.Date;
                    if (!isMeetingRoom)
                    {
                        decimal partial = WorkNest.Application.Services.BillingPeriods.MonthsBetween(periodStart, periodEnd);
                        if (partial > 0 && partial < recurringMonths) cycleMonths = partial;
                    }
                }

                int cMonthsCycle = (startOn.HasValue && endOn.HasValue && (endOn.Value - startOn.Value).TotalDays > 20) 
                    ? Math.Max(1, (int)Math.Round((endOn.Value - startOn.Value).TotalDays / 30.4375)) 
                    : 12;
                if (monthlyRent <= 0)
                {
                    if (subtotalAmount > 0 && cMonthsCycle > 0)
                    {
                        monthlyRent = Math.Round(subtotalAmount / cMonthsCycle, 2);
                    }
                    else if (totalAmount > 0 && cMonthsCycle > 0)
                    {
                        monthlyRent = Math.Round(totalAmount / cMonthsCycle, 2);
                    }
                }

                int contractMonths = (startOn.HasValue && endOn.HasValue && (endOn.Value - startOn.Value).TotalDays > 20) 
                    ? Math.Max(1, (int)Math.Round((endOn.Value - startOn.Value).TotalDays / 30.4375)) 
                    : 12;

                decimal grossAdvanceRent = Math.Round(monthlyRent * cycleMonths, 2);
                decimal appliedDiscount = 0m;
                if (discountPercentage > 0)
                {
                    appliedDiscount = Math.Round(grossAdvanceRent * (discountPercentage / 100.0m), 2);
                }
                else if (discountAmount > 0)
                {
                    // Fixed discount is per month, the same as the first invoice (InvoiceCalculationEngine):
                    // amount x months in this period; meeting rooms take it once.
                    appliedDiscount = Math.Round(Math.Min(isMeetingRoom ? discountAmount : discountAmount * cycleMonths, grossAdvanceRent), 2);
                }

                decimal discountedBase = Math.Max(0, grossAdvanceRent - appliedDiscount);
                decimal supportCharge;
                decimal taxRate = appliedTaxPercentage >= 0 ? appliedTaxPercentage : 16.00m;
                if (isMeetingRoom)
                {
                    supportCharge = Math.Round(discountedBase * 0.10m, 2);
                }
                else
                {
                    decimal rate = perSeatSupportRate > 0 ? perSeatSupportRate : 2000.00m;
                    int cap = spaceCapacity > 0 ? spaceCapacity : 1;
                    supportCharge = Math.Round(rate * cap * cycleMonths, 2);
                }
                if (supportCharge > discountedBase) supportCharge = discountedBase;   // support is part of the rent
                decimal taxTotal = Math.Round(supportCharge * (taxRate / 100.0m), 2);
                decimal roomRentExclusive = Math.Max(0, discountedBase - supportCharge);
                decimal effectiveTaxRateOnRent = (discountedBase > 0) 
                    ? Math.Round(taxTotal / discountedBase, 6) 
                    : 0m;

                decimal arrears = 0m;
                using (var arrCmd = new SqlCommand("dbo.WN_GetCustomerArrears", conn))
                {
                    arrCmd.CommandType = CommandType.StoredProcedure;
                    arrCmd.Parameters.AddWithValue("@UserId", userId);
                    var arrVal = await arrCmd.ExecuteScalarAsync();
                    if (arrVal != null && arrVal != DBNull.Value)
                    {
                        arrears = Convert.ToDecimal(arrVal);
                    }
                }

                DateTime today = _clock.Today;
                // Due 7 days after the invoice date (same rule as every invoice)
                DateTime recurringDueDate = today.AddDays(InvoiceDueDays);

                var dto = new CreateCustomInvoiceDto
                {
                    BookingId = bookingId,
                    UserId = userId,
                    IssuedOn = today,
                    DueOn = recurringDueDate,
                    Notes = $"Cycle Billing Invoice for Booking #{bookingId} - {spaceName}",
                    SendEmail = true,
                    BillingPeriodStart = periodStart,
                    BillingPeriodEnd = periodEnd,
                    AdvanceRentMonths = (int)Math.Max(1, Math.Round(cycleMonths)),
                    SecurityDepositMonths = secMonths,
                    SecurityDepositAmount = 0m,
                    RoomRentExclTax = roomRentExclusive,
                    ServiceCharges = supportCharge,
                    TaxOnServiceCharges = taxTotal,
                    SubTotal = grossAdvanceRent,
                    DiscountTotal = appliedDiscount,
                    TaxTotal = taxTotal,
                    GrandTotal = Math.Round(grossAdvanceRent - appliedDiscount + taxTotal, 2, MidpointRounding.AwayFromZero), // arrears shown, not added (each invoice is paid on its own)
                    RentAccountId = rentAccountId,
                    SecurityReceivedId = securityReceivedId,
                    ServicesIncomeId = servicesIncomeId,
                    SalesTaxId = salesTaxId,
                    AccountReceivableId = accountReceivableId,
                    AccountsCoaId = rentAccountId,
                    Lines = new List<CreateCustomInvoiceLineDto>()
                };

                if (grossAdvanceRent > 0)
                {
                    dto.Lines.Add(new CreateCustomInvoiceLineDto
                    {
                        Description = $"Rent for {periodStart:MMM d, yyyy} to {periodEnd:MMM d, yyyy}",
                        Quantity = 1,
                        UnitPrice = grossAdvanceRent - appliedDiscount,
                        DiscountAmount = 0m,
                        TaxRate = effectiveTaxRateOnRent,
                        ChargeTypeId = 1
                    });
                }

                // Earlier unpaid invoices stay open and are paid on their own, so the balance is shown
                // for the customer's information only - adding it here would count it twice.
                if (arrears > 0)
                {
                    dto.Notes = $"{dto.Notes}. Previous unpaid balance: PKR {arrears:N0} (separate invoices, not included in this total).";
                }

                return await CreateCustomInvoice(dto);
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
                using var cmd = new SqlCommand("dbo.WN_QueueInvoiceDeliveryFailure", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@InvoiceId", invoiceId);
                cmd.Parameters.AddWithValue("@BookingId", (object?)bookingId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TargetEmail", (object?)targetEmail ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@LastError", (object?)lastError ?? DBNull.Value);
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
        public DateTime IssuedOn { get; set; } = WorkNest.Application.Services.BusinessClock.Default.Today;
        public DateTime DueOn { get; set; } = WorkNest.Application.Services.BusinessClock.Default.Today.AddDays(InvoiceController.InvoiceDueDays);
        public string? CurrencyCode { get; set; } = "PKR";
        public string? Notes { get; set; }
        public bool SendEmail { get; set; } = true;
        public DateTime? BillingPeriodStart { get; set; }
        public DateTime? BillingPeriodEnd { get; set; }
        public int? AdvanceRentMonths { get; set; }
        public int? SecurityDepositMonths { get; set; }
        public decimal? SecurityDepositAmount { get; set; }
        public decimal? RoomRentExclTax { get; set; }
        public decimal? ServiceCharges { get; set; }
        public decimal? TaxOnServiceCharges { get; set; }
        public decimal? SubTotal { get; set; }
        public decimal? DiscountTotal { get; set; }
        public decimal? TaxTotal { get; set; }
        public decimal? GrandTotal { get; set; }
        public int? AccountReceivableId { get; set; }
        public int? RentAccountId { get; set; }
        public int? ServicesIncomeId { get; set; }
        public int? SalesTaxId { get; set; }
        public int? SecurityReceivedId { get; set; }
        public int? AccountsCoaId { get; set; }
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

    public class GenerateRecurringInvoiceRequest
    {
        public DateTime? BillingPeriodStart { get; set; }
        public DateTime? BillingPeriodEnd { get; set; }
        public bool AutoApproveAndSend { get; set; } = false;
        public bool SendIfAlreadyExists { get; set; } = false;
    }
}
