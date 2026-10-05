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

using WorkNest.API.Extensions;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    [ValidateLocationScope]
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

        private async Task<SqlConnection> OpenConnectionAsync()
        {
            var conn = new SqlConnection(GetConnectionString());
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conn);
            await cmd.ExecuteNonQueryAsync();
            return conn;
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
            [FromQuery] int? typeId = null,
            [FromQuery] int? locationId = null)
        {
            try
            {
                if (page <= 0) page = 1;
                if (limit <= 0) limit = 10;
                int offset = (page - 1) * limit;

                int? effectiveLocationId = locationId;
                if (User?.Identity?.IsAuthenticated == true && User.IsLocationBoundRole())
                {
                    var claimLocId = User.GetLocationId();
                    if (claimLocId.HasValue)
                    {
                        effectiveLocationId = claimLocId.Value;
                    }
                }

                using var conn = await OpenConnectionAsync();

                using var cmd = new SqlCommand("dbo.WN_GetInvoicesList", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Page", page);
                cmd.Parameters.AddWithValue("@Limit", limit);
                cmd.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TypeId", (object?)typeId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@LocationId", (object?)effectiveLocationId ?? DBNull.Value);

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

        [HttpGet("api/customer/{customerId:int}/invoices")]
        [HttpGet("api/customers/{customerId:int}/invoices")]
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

        [HttpGet("WorkNest/api/invoice/{id:int}/pdf")]
        [HttpGet("WorkNest/api/invoice/{id:int}/statement-pdf")]
        [HttpGet("WorkNest/api/invoice/{id:int}/download")]
        [HttpGet("api/invoice/{id:int}/pdf")]
        [HttpGet("api/invoice/{id:int}/statement-pdf")]
        [HttpGet("api/invoice/{id:int}/download")]
        [AllowAnonymous]
        public async Task<IActionResult> GetStatementInvoicePdf(int id)
        {
            try
            {
                using var conn = await OpenConnectionAsync();

                var dto = await BuildStatementInvoicePdfDtoAsync(id, conn);
                if (dto == null)
                    return NotFound(new { isSuccessful = false, message = "Invoice not found." });

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

        [HttpGet("WorkNest/api/invoice/sales-tax/{publicId:guid}/pdf")]
        [HttpGet("WorkNest/api/invoice/sales-tax/{publicId:guid}/download")]
        [HttpGet("api/invoice/sales-tax/{publicId:guid}/pdf")]
        [HttpGet("api/invoice/sales-tax/{publicId:guid}/download")]
        [AllowAnonymous]
        public async Task<IActionResult> GetSalesTaxPdf(Guid publicId)
        {
            try
            {
                var dto = await _db.GetCustomerSTInvoiceByPublicIdAsync(publicId);
                if (dto == null)
                {
                    using var conn = await OpenConnectionAsync();
                    string findInvoiceSql = @"
                        SELECT TOP 1 Id, InvoiceNumber, ISNULL(SubTotal, 0) AS SubTotal, ISNULL(DiscountTotal, 0) AS DiscountTotal, ISNULL(TaxTotal, 0) AS TaxTotal
                        FROM dbo.WN_Invoices WITH (NOLOCK)
                        WHERE PublicId = @PublicId;";

                    using var cmdFind = new SqlCommand(findInvoiceSql, conn);
                    cmdFind.Parameters.AddWithValue("@PublicId", publicId);
                    using var rFind = await cmdFind.ExecuteReaderAsync();
                    if (await rFind.ReadAsync())
                    {
                        int invId = rFind.GetInt32(0);
                        string invNum = rFind.GetString(1);
                        decimal subTotal = rFind.GetDecimal(2);
                        decimal discTotal = rFind.GetDecimal(3);
                        decimal taxTotal = rFind.GetDecimal(4);

                        rFind.Close();

                        string stInvoiceNumber = invNum.Replace("INV-", "ST-INV-");
                        decimal roomRentExcl = subTotal - discTotal;
                        decimal stTaxRate = taxTotal > 0 ? 16.00m : 0.00m;
                        decimal serviceChargeAmount = taxTotal > 0 ? Math.Round(taxTotal / 0.16m, 2) : 0m;
                        decimal roomRentAmount = Math.Max(0, roomRentExcl - serviceChargeAmount);
                        decimal stSubTotal = serviceChargeAmount;
                        decimal stGrandTotal = stSubTotal + taxTotal;

                        string insertStSql = @"
                            IF NOT EXISTS (SELECT 1 FROM dbo.WN_CustomerSTInvoice WHERE CustomerInvoiceId = @InvoiceId OR PublicId = @PublicId)
                            BEGIN
                                INSERT INTO dbo.WN_CustomerSTInvoice (
                                    PublicId, CustomerInvoiceId, STInvoiceNumber,
                                    TariffHeading, TariffLabel,
                                    RoomRentDescription, RoomRentAmount, RoomRentTaxRate, RoomRentTaxAmount,
                                    ServiceChargeDescription, ServiceChargeAmount, ServiceChargeTaxRate, ServiceChargeTaxAmount,
                                    SecurityDepositDescription, SecurityDepositAmount, SecurityDepositTaxRate, SecurityDepositTaxAmount,
                                    SubTotal, TaxTotal, GrandTotal, DocumentUrl, CreatedOn, CreatedById
                                )
                                VALUES (
                                    @PublicId, @InvoiceId, @STInvoiceNumber,
                                    '9805.9200', 'Business Support Services',
                                    'Room Rent (Exclusive of Service Charge)', @RoomRentAmount, 0.00, 0.00,
                                    'Service Charges', @ServiceChargeAmount, @STTaxRate, @TaxAmount,
                                    NULL, NULL, 0.00, 0.00,
                                    @SubTotal, @TaxAmount, @GrandTotal, NULL, SYSUTCDATETIME(), 1
                                );
                            END;";

                        using var stCmd = new SqlCommand(insertStSql, conn);
                        stCmd.Parameters.AddWithValue("@PublicId", publicId);
                        stCmd.Parameters.AddWithValue("@InvoiceId", invId);
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

        private static bool HasColumn(SqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool _invoicePdfSpUpdated = false;

        private async Task EnsureInvoicePdfSpUpdatedAsync(SqlConnection conn)
        {
            if (_invoicePdfSpUpdated) return;
            try
            {
                string sql = @"
CREATE OR ALTER PROCEDURE dbo.WN_GetStatementInvoicePdfData
    @InvoiceId INT
AS
BEGIN
    SELECT TOP 1
        i.Id,
        i.InvoiceNumber,
        i.UserId,
        i.BookingId,
        i.IssuedOn,
        i.DueOn,
        COALESCE(i.BillingPeriodStart, b.StartOn, i.IssuedOn) AS BillingPeriodStart,
        COALESCE(i.BillingPeriodEnd, b.EndOn, i.DueOn) AS BillingPeriodEnd,
        ISNULL(i.GrandTotal, 0) AS GrandTotal,
        ISNULL(i.PaidTotal, 0) AS PaidTotal,
        ISNULL(i.SubTotal, 0) AS SubTotal,
        ISNULL(i.DiscountTotal, 0) AS DiscountTotal,
        ISNULL(i.TaxTotal, 0) AS TaxTotal,
        ISNULL(i.CurrencyCode, 'PKR') AS CurrencyCode,
        COALESCE(NULLIF(LTRIM(RTRIM(c.Company)), ''), NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), NULLIF(LTRIM(RTRIM(bd.CustomerName)), ''), u.Name, '') AS AccountName,
        COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), NULLIF(LTRIM(RTRIM(bd.CustomerName)), ''), u.Name, '') AS AttnName,
        COALESCE(NULLIF(LTRIM(RTRIM(c.Address)), ''), NULLIF(LTRIM(RTRIM(u.Address)), ''), '') AS BillingAddress,
        COALESCE(NULLIF(LTRIM(RTRIM(c.Code)), ''), NULLIF(LTRIM(RTRIM(b.CustomerCode)), ''), NULLIF(LTRIM(RTRIM(bd.CustomerCode)), ''), '') AS AccountNumber,
        COALESCE(NULLIF(LTRIM(RTRIM(c.CnicOrPassport)), ''), NULLIF(LTRIM(RTRIM(c.NTN)), ''), NULLIF(LTRIM(RTRIM(u.CnicOrPassport)), ''), '') AS SntnNtnNic,
        COALESCE(loc.Name, br.[Description], comp.CompanyName, '') AS CenterName,
        COALESCE(comp.CompanyName, br.[Description], loc.Name, '') AS VendorLegalName,
        COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(comp.AddressLine1, '') + ' ' + ISNULL(comp.AddressLine2, ''))), ''), loc.Address, '') AS VendorAddress,
        COALESCE(comp.Contact, '') AS VendorPhone,
        COALESCE(comp.Fax, '') AS VendorFax,
        COALESCE(comp.NTN, '') AS VendorNtn,
        ISNULL(NULLIF(bd.AppliedChargePercentage, 0), 10.00) AS AppliedChargePercentage,
        ISNULL(NULLIF(bd.AppliedTaxPercentage, 0), 16.00) AS AppliedTaxPercentage,
        COALESCE(
            NULLIF(st.ServiceChargeAmount, 0),
            NULLIF(bd.SupportChargeAmount, 0),
            CASE WHEN ISNULL(i.TaxTotal, 0) > 0 THEN ROUND(i.TaxTotal / (ISNULL(NULLIF(bd.AppliedTaxPercentage, 0), 16.00) / 100.0), 2) ELSE 0 END
        ) AS SupportChargeAmount,
        COALESCE(NULLIF(b.SecurityDepositRequired, 0), NULLIF(i.SecurityDepositAmount, 0), ISNULL(bd.SecurityDeposit, 0)) AS SecurityDepositAmount,
        15.00 AS WithholdingTaxRate,
        st.PublicId AS STPublicId,
        st.STInvoiceNumber
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    LEFT JOIN dbo.WN_CustomerSTInvoice st WITH (NOLOCK) ON st.CustomerInvoiceId = i.Id
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    LEFT JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.BookingGuid = b.IdGUID
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON (
        (b.CustomerCode IS NOT NULL AND b.CustomerCode <> '' AND c.Code = b.CustomerCode)
        OR (bd.CustomerCode IS NOT NULL AND bd.CustomerCode <> '' AND c.Code = bd.CustomerCode)
        OR (i.UserId IS NOT NULL AND i.UserId > 0 AND c.UserId = i.UserId)
        OR (u.Email IS NOT NULL AND u.Email <> '' AND c.Email = u.Email)
    ) AND (c.IsActive = 1 OR c.IsActive IS NULL)
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
    LEFT JOIN dbo.WN_Locations loc WITH (NOLOCK) ON loc.Id = s.LocationId
    LEFT JOIN dbo.Branches br WITH (NOLOCK) ON br.Id = loc.BranchId
    LEFT JOIN dbo.Company comp WITH (NOLOCK) ON comp.Id = COALESCE(loc.CompanyId, br.CompanyId, u.CompanyId)
    WHERE i.Id = @InvoiceId;

    SELECT 
        l.ChargeTypeId,
        l.Description,
        ISNULL(l.Quantity, 1) AS Quantity,
        ISNULL(l.UnitPrice, 0) AS UnitPrice,
        (l.Quantity * l.UnitPrice - l.DiscountAmount) AS PriceExclVat,
        l.TaxAmount AS VatAmount,
        l.LineTotal AS TotalInclVat,
        l.TaxRate,
        ISNULL(ct.Label, 'Business Support Services') AS CategoryName
    FROM dbo.WN_InvoiceLines l WITH (NOLOCK)
    LEFT JOIN dbo.WN_ChargeTypes ct WITH (NOLOCK) ON ct.Id = l.ChargeTypeId
    WHERE l.InvoiceId = @InvoiceId
    ORDER BY l.SortOrder, l.Id;

    DECLARE @UserId INT;
    SELECT @UserId = UserId FROM dbo.WN_Invoices WHERE Id = @InvoiceId;

    SELECT 
        ISNULL(SUM(GrandTotal - PaidTotal), 0) AS PriorBalance,
        ISNULL(SUM(PaidTotal), 0) AS PaymentReceived
    FROM dbo.WN_Invoices WITH (NOLOCK)
    WHERE UserId = @UserId AND Id < @InvoiceId;

    SELECT TOP 1 Description AS BankName, ShortDesc AS BankAccountNumber
    FROM dbo.AccountsCOA WITH (NOLOCK)
    WHERE AccountNature = 'Bank' OR Description LIKE '%Bank%';
END;";
                using var cmdSp = new SqlCommand(sql, conn);
                await cmdSp.ExecuteNonQueryAsync();
                _invoicePdfSpUpdated = true;
            }
            catch { }
        }

        private async Task<StatementInvoicePdfDto?> BuildStatementInvoicePdfDtoAsync(int id, SqlConnection conn)
        {
            await EnsureInvoicePdfSpUpdatedAsync(conn);
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
                    if (HasColumn(reader, "WithholdingTaxRate") && !reader.IsDBNull(reader.GetOrdinal("WithholdingTaxRate")))
                        dto.WithholdingTaxRate = reader.GetDecimal(reader.GetOrdinal("WithholdingTaxRate"));
                    if (HasColumn(reader, "STPublicId") && !reader.IsDBNull(reader.GetOrdinal("STPublicId")))
                    {
                        var stPublicId = reader.GetGuid(reader.GetOrdinal("STPublicId"));
                        dto.SupportChargesInvoiceUrl = $"/api/invoice/sales-tax/{stPublicId}/pdf";
                    }
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

            decimal totalLineVat = dto.LineItems.Sum(i => i.VatAmount);
            bool hasTax = totalLineVat > 0 
                       || (dto.AppliedTaxPercentage.HasValue && dto.AppliedTaxPercentage.Value > 0)
                       || (dto.SupportChargeAmount.HasValue && dto.SupportChargeAmount.Value > 0);

            if (!hasTax && totalLineVat == 0)
            {
                dto.AppliedTaxPercentage = 0m;
                dto.AppliedChargePercentage = 0m;
                dto.SupportChargesInvoiceUrl = null;
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

                string autoInsertSql = @"
                    IF NOT EXISTS (SELECT 1 FROM dbo.WN_CustomerSTInvoice WHERE CustomerInvoiceId = @InvoiceId)
                    BEGIN
                        INSERT INTO dbo.WN_CustomerSTInvoice (
                            PublicId, CustomerInvoiceId, STInvoiceNumber,
                            TariffHeading, TariffLabel,
                            RoomRentDescription, RoomRentAmount, RoomRentTaxRate, RoomRentTaxAmount,
                            ServiceChargeDescription, ServiceChargeAmount, ServiceChargeTaxRate, ServiceChargeTaxAmount,
                            SecurityDepositDescription, SecurityDepositAmount, SecurityDepositTaxRate, SecurityDepositTaxAmount,
                            SubTotal, TaxTotal, GrandTotal, DocumentUrl, CreatedOn, CreatedById
                        )
                        VALUES (
                            NEWID(), @InvoiceId, @STInvoiceNumber,
                            '9805.9200', 'Business Support Services',
                            'Room Rent (Exclusive of Service Charge)', @RoomRentAmount, 0.00, 0.00,
                            'Service Charges', @ServiceChargeAmount, @STTaxRate, @TaxAmount,
                            NULL, NULL, 0.00, 0.00,
                            @SubTotal, @TaxAmount, @GrandTotal, NULL, SYSUTCDATETIME(), 1
                        );
                    END;";

                using var autoStCmd = new SqlCommand(autoInsertSql, conn);
                autoStCmd.Parameters.AddWithValue("@InvoiceId", id);
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

            if (stPublicId.HasValue && hasTax)
            {
                var baseUrl = _config["Application:PublicBaseUrl"];
                if ((string.IsNullOrWhiteSpace(baseUrl) || baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)) && Request != null)
                {
                    baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
                }
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    baseUrl = "http://localhost:5200";
                }

                var cleanBase = baseUrl.TrimEnd('/');
                var pathPrefix = cleanBase.EndsWith("/WorkNest", StringComparison.OrdinalIgnoreCase) ? "" : "/WorkNest";
                dto.SupportChargesInvoiceUrl = $"{cleanBase}{pathPrefix}/api/invoice/sales-tax/{stPublicId.Value}/pdf";
            }
            else
            {
                dto.SupportChargesInvoiceUrl = null;
            }
            }

            return dto;
        }

        [HttpGet("api/invoice/{id:int}")]
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

                if (req.DiscountTotal.HasValue && req.DiscountTotal.Value > 0 && discountTotal == 0)
                {
                    discountTotal = req.DiscountTotal.Value;
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
                        SecurityDepositMonths = @SecurityDepositMonths,
                        SecurityDepositAmount = @SecurityDepositAmount,
                        RoomRentExclTax = @RoomRentExclTax,
                        ServiceCharges = @ServiceCharges,
                        TaxOnServiceCharges = @TaxOnServiceCharges,
                        RentAccountId = COALESCE(@RentAccountId, RentAccountId),
                        SecurityReceivedId = COALESCE(@SecurityReceivedId, SecurityReceivedId),
                        ServicesIncomeId = COALESCE(@ServicesIncomeId, ServicesIncomeId),
                        SalesTaxId = COALESCE(@SalesTaxId, SalesTaxId),
                        AccountReceivableId = COALESCE(@AccountReceivableId, AccountReceivableId),
                        AccountsCoaId = COALESCE(@AccountsCoaId, AccountsCoaId, @RentAccountId),
                        SubTotal = @SubTotal
                    WHERE Id = @InvoiceId;";
                using var updateCmd = new SqlCommand(updateHeaderSql, conn);
                updateCmd.Parameters.AddWithValue("@InvoiceId", newInvoiceId);
                updateCmd.Parameters.AddWithValue("@BillingPeriodStart", (object?)req.BillingPeriodStart ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@BillingPeriodEnd", (object?)req.BillingPeriodEnd ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@AdvanceRentMonths", (object?)req.AdvanceRentMonths ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@SecurityDepositMonths", (object?)req.SecurityDepositMonths ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@SecurityDepositAmount", req.SecurityDepositAmount ?? 0m);
                updateCmd.Parameters.AddWithValue("@RoomRentExclTax", roomRentAmount);
                updateCmd.Parameters.AddWithValue("@ServiceCharges", serviceChargeAmount);
                updateCmd.Parameters.AddWithValue("@TaxOnServiceCharges", taxOnServiceCharges);
                updateCmd.Parameters.AddWithValue("@RentAccountId", (object?)req.RentAccountId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@SecurityReceivedId", (object?)req.SecurityReceivedId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@ServicesIncomeId", (object?)req.ServicesIncomeId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@SalesTaxId", (object?)req.SalesTaxId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@AccountReceivableId", (object?)req.AccountReceivableId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@AccountsCoaId", (object?)req.AccountsCoaId ?? (object?)req.RentAccountId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@SubTotal", subTotal - discountTotal);
                await updateCmd.ExecuteNonQueryAsync();

                string stInvoiceNumber = invoiceNumber.Replace("INV-", "ST-INV-");
                decimal stTaxRate = taxTotal > 0 ? 16.00m : 0.00m;
                decimal stSubTotal = serviceChargeAmount;
                decimal stGrandTotal = stSubTotal + taxTotal;

                string insertStSql = @"
                    IF NOT EXISTS (SELECT 1 FROM dbo.WN_CustomerSTInvoice WHERE CustomerInvoiceId = @InvoiceId)
                    BEGIN
                        INSERT INTO dbo.WN_CustomerSTInvoice (
                            PublicId, CustomerInvoiceId, STInvoiceNumber,
                            TariffHeading, TariffLabel,
                            RoomRentDescription, RoomRentAmount, RoomRentTaxRate, RoomRentTaxAmount,
                            ServiceChargeDescription, ServiceChargeAmount, ServiceChargeTaxRate, ServiceChargeTaxAmount,
                            SecurityDepositDescription, SecurityDepositAmount, SecurityDepositTaxRate, SecurityDepositTaxAmount,
                            SubTotal, TaxTotal, GrandTotal, DocumentUrl, CreatedOn, CreatedById
                        )
                        VALUES (
                            NEWID(), @InvoiceId, @STInvoiceNumber,
                            '9805.9200', 'Business Support Services',
                            'Room Rent (Exclusive of Service Charge)', @RoomRentAmount, 0.00, 0.00,
                            'Service Charges', @ServiceChargeAmount, @STTaxRate, @TaxAmount,
                            NULL, NULL, 0.00, 0.00,
                            @SubTotal, @TaxAmount, @GrandTotal, NULL, SYSUTCDATETIME(), 1
                        );
                    END;";

                using var stCmd = new SqlCommand(insertStSql, conn);
                stCmd.Parameters.AddWithValue("@InvoiceId", newInvoiceId);
                stCmd.Parameters.AddWithValue("@STInvoiceNumber", stInvoiceNumber);
                stCmd.Parameters.AddWithValue("@RoomRentAmount", roomRentAmount);
                stCmd.Parameters.AddWithValue("@ServiceChargeAmount", serviceChargeAmount);
                stCmd.Parameters.AddWithValue("@STTaxRate", stTaxRate);
                stCmd.Parameters.AddWithValue("@SubTotal", stSubTotal);
                stCmd.Parameters.AddWithValue("@TaxAmount", taxTotal);
                stCmd.Parameters.AddWithValue("@GrandTotal", stGrandTotal);
                await stCmd.ExecuteNonQueryAsync();

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
                            emailNotice = $" Email dispatched with PDF attached to {targetEmail}.";
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
                using var conn = await OpenConnectionAsync();

                string invoiceNumber = "";
                string targetEmail = "";
                string customerName = "";
                string spaceName = "WorkNest Workspace";
                decimal grandTotal = 0, subTotal = 0, taxTotal = 0, discountTotal = 0, securityDeposit = 0;
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
                            decimal secDep = HasColumn(reader, "SecurityDepositAmount") && !reader.IsDBNull(reader.GetOrdinal("SecurityDepositAmount"))
                                ? reader.GetDecimal(reader.GetOrdinal("SecurityDepositAmount"))
                                : 0m;
                            securityDeposit = secDep;
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
                    return StatusCode(500, new { isSuccessful = false, message = "Failed to generate Invoice PDF attachment. Please ensure dbo.WN_GetStatementInvoicePdfData is updated in the database." });
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
                        pdfBytes: pdfBytes
                    );

                    return Ok(new { isSuccessful = true, message = $"Invoice email sent successfully to {targetEmail} with Statement PDF attached." });
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
                using var conn = await OpenConnectionAsync();

                string bookingSql = @"
                    SELECT TOP 1 
                        b.Id AS BookingId, b.UserId, b.StartOn, b.EndOn, b.MonthlyRent, b.SubtotalAmount, b.TotalAmount,
                        b.BillingPeriodMonths, b.SecurityDepositMonths, b.DiscountAmount, b.DiscountPercentage, b.DiscountType,
                        b.SecurityDepositRequired,
                        COALESCE(NULLIF(b.SecurityDepositRequired, 0), NULLIF(bd.SecurityDeposit, 0), 0) AS ResolvedSecurityDeposit,
                        u.Email AS CustomerEmail, ISNULL(NULLIF(c.Company, ''), ISNULL(NULLIF(u.Name, ''), 'Valued Customer')) AS CustomerName,
                        s.Name AS SpaceName, ISNULL(st.Name, '') AS SpaceTypeName, ISNULL(st.Description, '') AS CategoryCode,
                        ISNULL(s.Capacity, 1) AS SpaceCapacity,
                        ISNULL(bd.AppliedTaxPercentage, 16.00) AS AppliedTaxPercentage,
                        ISNULL(bd.AppliedChargePercentage, 10.00) AS AppliedChargePercentage,
                        COALESCE(bd.PerSeatSupportRate, (SELECT TOP 1 FixedAmount FROM dbo.WN_ChargeTypeRate WITH (NOLOCK) WHERE ChargeTypeId = 4 AND (StartDate IS NULL OR StartDate <= b.StartOn) AND (EndDate IS NULL OR EndDate >= b.StartOn) ORDER BY StartDate DESC), (SELECT TOP 1 FixedAmount FROM dbo.WN_ChargeTypeRate WITH (NOLOCK) WHERE ChargeTypeId = 4 ORDER BY StartDate DESC), 2000.00) AS PerSeatSupportRate,
                        COALESCE(b.RentAccountId, bd.RentAccountId, 2852) AS RentAccountId,
                        COALESCE(b.SecurityReceivedId, bd.SecurityReceivedId, 76) AS SecurityReceivedId,
                        COALESCE(b.ServicesIncomeId, bd.ServicesIncomeId, 2853) AS ServicesIncomeId,
                        COALESCE(b.SalesTaxId, bd.SalesTaxId, 2854) AS SalesTaxId,
                        COALESCE(b.AccountReceivableId, bd.AccountReceivableId, 2855) AS AccountReceivableId
                    FROM dbo.WN_Bookings b WITH (NOLOCK)
                    LEFT JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.BookingGuid = b.IdGUID
                    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = b.UserId
                    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = b.UserId
                    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                    LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
                    WHERE b.Id = @BookingId;";

                int userId = 0;
                decimal monthlyRent = 0, subtotalAmount = 0, totalAmount = 0, discountAmount = 0, discountPercentage = 0, secDepositReq = 0, resolvedSecDep = 0;
                int billingMonths = 3, secMonths = 2, spaceCapacity = 1;
                decimal appliedTaxPercentage = 16.00m, appliedChargePercentage = 10.00m, perSeatSupportRate = 2000.00m;
                int? rentAccountId = null, securityReceivedId = null, servicesIncomeId = null, salesTaxId = null, accountReceivableId = null;
                string discountType = "Percentage";
                DateTime? startOn = null, endOn = null;
                string customerEmail = "", customerName = "", spaceName = "Workspace", spaceTypeName = "", categoryCode = "";

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
                        subtotalAmount = reader.IsDBNull(reader.GetOrdinal("SubtotalAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("SubtotalAmount"));
                        totalAmount = reader.IsDBNull(reader.GetOrdinal("TotalAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("TotalAmount"));
                        discountAmount = reader.IsDBNull(reader.GetOrdinal("DiscountAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("DiscountAmount"));
                        discountPercentage = reader.IsDBNull(reader.GetOrdinal("DiscountPercentage")) ? 0 : reader.GetDecimal(reader.GetOrdinal("DiscountPercentage"));
                        discountType = reader.IsDBNull(reader.GetOrdinal("DiscountType")) ? "Percentage" : reader.GetString(reader.GetOrdinal("DiscountType"));
                        secDepositReq = reader.IsDBNull(reader.GetOrdinal("SecurityDepositRequired")) ? 0 : reader.GetDecimal(reader.GetOrdinal("SecurityDepositRequired"));
                        resolvedSecDep = HasColumn(reader, "ResolvedSecurityDeposit") && !reader.IsDBNull(reader.GetOrdinal("ResolvedSecurityDeposit")) ? reader.GetDecimal(reader.GetOrdinal("ResolvedSecurityDeposit")) : 0;
                        billingMonths = reader.IsDBNull(reader.GetOrdinal("BillingPeriodMonths")) ? 3 : reader.GetInt32(reader.GetOrdinal("BillingPeriodMonths"));
                        secMonths = reader.IsDBNull(reader.GetOrdinal("SecurityDepositMonths")) ? 2 : reader.GetInt32(reader.GetOrdinal("SecurityDepositMonths"));
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
                    SecurityDeposit = secDepositReq > 0 ? secDepositReq : resolvedSecDep,
                    SecurityDepositMonths = secMonths,
                    PerSeatSupportRate = perSeatSupportRate,
                    AppliedTaxPercentage = appliedTaxPercentage,
                    DiscountType = discountType,
                    DiscountPercentage = discountPercentage,
                    DiscountAmount = discountAmount,
                    StartOn = startOn,
                    EndOn = endOn
                };

                var calc = WorkNest.Application.Services.InvoiceCalculationEngine.CalculateInvoice(calcReq);

                DateTime periodStart = calc.BillingPeriodStart;
                DateTime periodEnd = calc.BillingPeriodEnd;
                decimal grossAdvanceRent = calc.Rent;
                decimal appliedDiscount = calc.Discount;
                decimal taxTotal = calc.Tax;
                decimal supportCharge = calc.ServiceCharge;
                decimal securityDeposit = calc.DepositFirstInstallment;
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

                string checkSql = "SELECT TOP 1 Id, InvoiceNumber FROM dbo.WN_Invoices WHERE BookingId = @BookingId ORDER BY Id ASC;";
                int existingId = 0;
                using (var checkCmd = new SqlCommand(checkSql, conn))
                {
                    checkCmd.Parameters.AddWithValue("@BookingId", bookingId);
                    using var r = await checkCmd.ExecuteReaderAsync();
                    if (await r.ReadAsync())
                    {
                        existingId = r.GetInt32(0);
                    }
                }

                if (existingId > 0)
                {
                    string updateSql = @"
                        UPDATE dbo.WN_Invoices
                        SET SubTotal = @SubTotal,
                            DiscountTotal = @DiscountTotal,
                            TaxTotal = @TaxTotal,
                            GrandTotal = @GrandTotal,
                            BillingPeriodStart = @BillingPeriodStart,
                            BillingPeriodEnd = @BillingPeriodEnd,
                            AdvanceRentMonths = @AdvanceRentMonths,
                            SecurityDepositMonths = @SecurityDepositMonths,
                            SecurityDepositAmount = @SecurityDepositAmount,
                            RoomRentExclTax = @RoomRentExclTax,
                            ServiceCharges = @ServiceCharges,
                            TaxOnServiceCharges = @TaxOnServiceCharges,
                            RentAccountId = COALESCE(@RentAccountId, RentAccountId),
                            SecurityReceivedId = COALESCE(@SecurityReceivedId, SecurityReceivedId),
                            ServicesIncomeId = COALESCE(@ServicesIncomeId, ServicesIncomeId),
                            SalesTaxId = COALESCE(@SalesTaxId, SalesTaxId),
                            AccountReceivableId = COALESCE(@AccountReceivableId, AccountReceivableId),
                            AccountsCoaId = COALESCE(@AccountsCoaId, AccountsCoaId, @RentAccountId),
                            DueOn = @DueOn,
                            UpdatedOn = SYSUTCDATETIME()
                        WHERE Id = @InvoiceId;";
                    using (var upCmd = new SqlCommand(updateSql, conn))
                    {
                        upCmd.Parameters.AddWithValue("@InvoiceId", existingId);
                        upCmd.Parameters.AddWithValue("@SubTotal", expectedSubtotal);
                        upCmd.Parameters.AddWithValue("@DiscountTotal", appliedDiscount);
                        upCmd.Parameters.AddWithValue("@TaxTotal", taxTotal);
                        upCmd.Parameters.AddWithValue("@GrandTotal", expectedGrandTotal);
                        upCmd.Parameters.AddWithValue("@BillingPeriodStart", periodStart);
                        upCmd.Parameters.AddWithValue("@BillingPeriodEnd", periodEnd);
                        upCmd.Parameters.AddWithValue("@AdvanceRentMonths", calc.BillingPeriodMonths);
                        upCmd.Parameters.AddWithValue("@SecurityDepositMonths", (object?)secMonths ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@SecurityDepositAmount", securityDeposit);
                        upCmd.Parameters.AddWithValue("@RoomRentExclTax", roomRentExclusive);
                        upCmd.Parameters.AddWithValue("@ServiceCharges", supportCharge);
                        upCmd.Parameters.AddWithValue("@TaxOnServiceCharges", taxTotal);
                        upCmd.Parameters.AddWithValue("@RentAccountId", (object?)rentAccountId ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@SecurityReceivedId", (object?)securityReceivedId ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@ServicesIncomeId", (object?)servicesIncomeId ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@SalesTaxId", (object?)salesTaxId ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@AccountReceivableId", (object?)accountReceivableId ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@AccountsCoaId", (object?)rentAccountId ?? DBNull.Value);
                        upCmd.Parameters.AddWithValue("@DueOn", startOn.HasValue ? startOn.Value : DateTime.Today);
                        await upCmd.ExecuteNonQueryAsync();
                    }

                    using (var delCmd = new SqlCommand("DELETE FROM dbo.WN_InvoiceLines WHERE InvoiceId = @InvoiceId;", conn))
                    {
                        delCmd.Parameters.AddWithValue("@InvoiceId", existingId);
                        await delCmd.ExecuteNonQueryAsync();
                    }

                    string insertLineSql = @"
                        INSERT INTO dbo.WN_InvoiceLines (
                            InvoiceId, ChargeTypeId, Description, Quantity,
                            UnitPrice, DiscountAmount, TaxRate, SortOrder
                        )
                        VALUES (
                            @InvoiceId, 1, @Description, 1,
                            @UnitPrice, @DiscountAmount, @TaxRate, 1
                        );";
                    using (var insCmd = new SqlCommand(insertLineSql, conn))
                    {
                        insCmd.Parameters.AddWithValue("@InvoiceId", existingId);
                        insCmd.Parameters.AddWithValue("@Description", mainLineDescription);
                        insCmd.Parameters.AddWithValue("@UnitPrice", grossAdvanceRent);
                        insCmd.Parameters.AddWithValue("@DiscountAmount", appliedDiscount);
                        insCmd.Parameters.AddWithValue("@TaxRate", 0m);
                        await insCmd.ExecuteNonQueryAsync();
                    }

                    if (securityDeposit > 0)
                    {
                        string insertDepSql = @"
                            INSERT INTO dbo.WN_InvoiceLines (
                                InvoiceId, ChargeTypeId, Description, Quantity,
                                UnitPrice, DiscountAmount, TaxRate, SortOrder
                            )
                            VALUES (
                                @InvoiceId, 2, @Description, @Quantity,
                                @UnitPrice, 0, 0, 2
                            );";
                        using (var depCmd = new SqlCommand(insertDepSql, conn))
                        {
                            depCmd.Parameters.AddWithValue("@InvoiceId", existingId);
                            depCmd.Parameters.AddWithValue("@Description", $"Security Deposit ({secMonths} Month(s) Refundable - {spaceName})");
                            depCmd.Parameters.AddWithValue("@Quantity", secMonths > 0 ? secMonths : 1);
                            depCmd.Parameters.AddWithValue("@UnitPrice", secMonths > 0 ? Math.Round(securityDeposit / secMonths, 2, MidpointRounding.AwayFromZero) : securityDeposit);
                            await depCmd.ExecuteNonQueryAsync();
                        }
                    }

                    return await SendInvoiceEmail(existingId);
                }

                var dto = new CreateCustomInvoiceDto
                {
                    BookingId = bookingId,
                    UserId = userId,
                    IssuedOn = DateTime.Today,
                    DueOn = startOn.HasValue ? startOn.Value : DateTime.Today,
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

                if (grossAdvanceRent > 0)
                {
                    dto.Lines.Add(new CreateCustomInvoiceLineDto
                    {
                        Description = mainLineDescription,
                        Quantity = 1,
                        UnitPrice = grossAdvanceRent,
                        DiscountAmount = appliedDiscount,
                        TaxRate = 0m,
                        ChargeTypeId = 1
                    });
                }

                if (securityDeposit > 0)
                {
                    dto.Lines.Add(new CreateCustomInvoiceLineDto
                    {
                        Description = $"Security Deposit ({secMonths} Month(s) Refundable - {spaceName})",
                        Quantity = secMonths > 0 ? secMonths : 1,
                        UnitPrice = secMonths > 0 ? Math.Round(securityDeposit / secMonths, 2, MidpointRounding.AwayFromZero) : securityDeposit,
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
                using var conn = await OpenConnectionAsync();

                string bookingSql = @"
                    SELECT TOP 1 
                        b.Id AS BookingId, b.UserId, b.StartOn, b.EndOn, b.MonthlyRent, b.SubtotalAmount, b.TotalAmount,
                        b.BillingPeriodMonths, b.SecurityDepositMonths, b.DiscountAmount, b.DiscountPercentage, b.DiscountType,
                        u.Email AS CustomerEmail, ISNULL(NULLIF(c.Company, ''), ISNULL(NULLIF(u.Name, ''), 'Valued Customer')) AS CustomerName,
                        s.Name AS SpaceName, ISNULL(st.Name, '') AS SpaceTypeName, ISNULL(st.Description, '') AS CategoryCode,
                        ISNULL(s.Capacity, 1) AS SpaceCapacity,
                        ISNULL(bd.AppliedTaxPercentage, 16.00) AS AppliedTaxPercentage,
                        ISNULL(bd.AppliedChargePercentage, 10.00) AS AppliedChargePercentage,
                        ISNULL(bd.PerSeatSupportRate, 2000.00) AS PerSeatSupportRate,
                        COALESCE(b.RentAccountId, bd.RentAccountId, 2852) AS RentAccountId,
                        COALESCE(b.SecurityReceivedId, bd.SecurityReceivedId, 76) AS SecurityReceivedId,
                        COALESCE(b.ServicesIncomeId, bd.ServicesIncomeId, 2853) AS ServicesIncomeId,
                        COALESCE(b.SalesTaxId, bd.SalesTaxId, 2854) AS SalesTaxId,
                        COALESCE(b.AccountReceivableId, bd.AccountReceivableId, 2855) AS AccountReceivableId
                    FROM dbo.WN_Bookings b WITH (NOLOCK)
                    LEFT JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.BookingGuid = b.IdGUID
                    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = b.UserId
                    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = b.UserId
                    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                    LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
                    WHERE b.Id = @BookingId;";

                int userId = 0;
                decimal monthlyRent = 0, subtotalAmount = 0, discountAmount = 0, discountPercentage = 0, totalAmount = 0;
                int billingMonths = 3, secMonths = 2, spaceCapacity = 1;
                decimal appliedTaxPercentage = 16.00m, appliedChargePercentage = 10.00m, perSeatSupportRate = 2000.00m;
                int? rentAccountId = null, securityReceivedId = null, servicesIncomeId = null, salesTaxId = null, accountReceivableId = null;
                DateTime? startOn = null, endOn = null;
                string customerEmail = "", customerName = "", spaceName = "Workspace", spaceTypeName = "", categoryCode = "";

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
                        subtotalAmount = reader.IsDBNull(reader.GetOrdinal("SubtotalAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("SubtotalAmount"));
                        totalAmount = reader.IsDBNull(reader.GetOrdinal("TotalAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("TotalAmount"));
                        discountAmount = reader.IsDBNull(reader.GetOrdinal("DiscountAmount")) ? 0 : reader.GetDecimal(reader.GetOrdinal("DiscountAmount"));
                        discountPercentage = reader.IsDBNull(reader.GetOrdinal("DiscountPercentage")) ? 0 : reader.GetDecimal(reader.GetOrdinal("DiscountPercentage"));
                        billingMonths = reader.IsDBNull(reader.GetOrdinal("BillingPeriodMonths")) ? 3 : reader.GetInt32(reader.GetOrdinal("BillingPeriodMonths"));
                        secMonths = HasColumn(reader, "SecurityDepositMonths") && !reader.IsDBNull(reader.GetOrdinal("SecurityDepositMonths")) ? Convert.ToInt32(reader["SecurityDepositMonths"]) : 2;
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

                decimal discountedBase = Math.Max(0, grossAdvanceRent - appliedDiscount);
                decimal supportCharge;
                decimal taxRate = appliedTaxPercentage > 0 ? appliedTaxPercentage : 16.00m;
                if (isMeetingRoom)
                {
                    supportCharge = Math.Round(discountedBase * 0.10m, 2);
                }
                else
                {
                    decimal rate = perSeatSupportRate > 0 ? perSeatSupportRate : 2000.00m;
                    int cap = spaceCapacity > 0 ? spaceCapacity : 1;
                    supportCharge = Math.Round(rate * cap * billingMonths, 2);
                }
                decimal taxTotal = Math.Round(supportCharge * (taxRate / 100.0m), 2);
                decimal roomRentExclusive = Math.Max(0, discountedBase - supportCharge);
                decimal effectiveTaxRateOnRent = (discountedBase > 0) 
                    ? Math.Round(taxTotal / discountedBase, 6) 
                    : 0m;

                decimal arrears = 0m;
                // Arrears = invoices not Paid and not Cancelled/void (legacy 2 / 5 or the OrderStatus "Paid" / "Cancelled" IDs).
                string arrearsSql = @"SELECT ISNULL(SUM(GrandTotal - PaidTotal), 0) FROM dbo.WN_Invoices WITH (NOLOCK)
                                      WHERE UserId = @UserId AND StatusId NOT IN (2, 5)
                                        AND StatusId NOT IN (SELECT Id FROM dbo.OrderStatus WITH (NOLOCK) WHERE LTRIM(RTRIM(Description)) IN ('Paid', 'Cancelled'));";
                using (var arrCmd = new SqlCommand(arrearsSql, conn))
                {
                    arrCmd.Parameters.AddWithValue("@UserId", userId);
                    var arrVal = await arrCmd.ExecuteScalarAsync();
                    if (arrVal != null && arrVal != DBNull.Value)
                    {
                        arrears = Convert.ToDecimal(arrVal);
                    }
                }

                DateTime today = DateTime.Today;
                // Recurring invoice due on the last day of the going month
                DateTime recurringDueDate = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

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
                    AdvanceRentMonths = billingMonths,
                    SecurityDepositMonths = secMonths,
                    SecurityDepositAmount = 0m,
                    RoomRentExclTax = roomRentExclusive,
                    ServiceCharges = supportCharge,
                    TaxOnServiceCharges = taxTotal,
                    SubTotal = grossAdvanceRent,
                    DiscountTotal = appliedDiscount,
                    TaxTotal = taxTotal,
                    GrandTotal = Math.Round(grossAdvanceRent - appliedDiscount + taxTotal + arrears, 2, MidpointRounding.AwayFromZero),
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
                        UnitPrice = grossAdvanceRent,
                        DiscountAmount = appliedDiscount,
                        TaxRate = effectiveTaxRateOnRent,
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
}
