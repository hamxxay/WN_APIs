using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;
using WorkNest.Infrastructure.ExternalServices.Pdf;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Background service that periodically checks WN_InvoiceDeliveryQueue for failed invoice email deliveries
    /// and retries sending them using exponential backoff (5m -> 15m -> 1h). Capped at 3 attempts.
    /// </summary>
    public class InvoiceDeliveryRetryService : BackgroundService
    {
        private readonly ILogger<InvoiceDeliveryRetryService> _logger;
        private readonly string _connectionString;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

        public InvoiceDeliveryRetryService(
            ILogger<InvoiceDeliveryRetryService> logger,
            IConfiguration configuration,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("DefaultConnection string is missing.");
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WorkNest Invoice Delivery Retry Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingDeliveryQueueAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during invoice delivery retry cycle.");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("WorkNest Invoice Delivery Retry Service stopping.");
        }

        private async Task ProcessPendingDeliveryQueueAsync(CancellationToken stoppingToken)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(stoppingToken);
            using var setCmd = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conn);
            await setCmd.ExecuteNonQueryAsync(stoppingToken);

            var pendingItems = new List<QueueItemDto>();

            string query = @"
                SELECT Id, InvoiceId, BookingId, TargetEmail, Attempts 
                FROM dbo.WN_InvoiceDeliveryQueue 
                WHERE Status = 'Pending' 
                  AND (NextRetryAt IS NULL OR NextRetryAt <= SYSUTCDATETIME());";

            using (var cmd = new SqlCommand(query, conn))
            using (var reader = await cmd.ExecuteReaderAsync(stoppingToken))
            {
                while (await reader.ReadAsync(stoppingToken))
                {
                    pendingItems.Add(new QueueItemDto
                    {
                        Id = reader.GetInt32(0),
                        InvoiceId = reader.GetInt32(1),
                        BookingId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                        TargetEmail = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Attempts = reader.GetInt32(4)
                    });
                }
            }

            if (pendingItems.Count == 0) return;

            _logger.LogInformation("Found {Count} pending invoice email delivery item(s) to retry.", pendingItems.Count);

            using var scope = _serviceProvider.CreateScope();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            foreach (var item in pendingItems)
            {
                if (stoppingToken.IsCancellationRequested) break;

                int attemptNum = item.Attempts + 1;
                try
                {
                    _logger.LogInformation("Retrying email delivery for Invoice ID {InvoiceId} (Attempt {Attempt}/3)...", item.InvoiceId, attemptNum);

                    string invoiceNumber = "";
                    string targetEmail = item.TargetEmail ?? "";
                    string customerName = "Valued Customer";
                    string spaceName = "WorkNest Workspace";
                    decimal grandTotal = 0, subTotal = 0, taxTotal = 0, discountTotal = 0;
                    var businessToday = _serviceProvider.GetRequiredService<IBusinessClock>().Today;
                    DateTime issuedOn = businessToday, dueOn = businessToday;
                    bool invoiceFound = false;

                    using (var emailCmd = new SqlCommand("dbo.WN_GetInvoiceEmailData", conn))
                    {
                        emailCmd.CommandType = CommandType.StoredProcedure;
                        emailCmd.Parameters.AddWithValue("@InvoiceId", item.InvoiceId);

                        using var r = await emailCmd.ExecuteReaderAsync(stoppingToken);
                        if (await r.ReadAsync(stoppingToken))
                        {
                            invoiceFound = true;
                            invoiceNumber = r.GetString(r.GetOrdinal("InvoiceNumber"));
                            if (string.IsNullOrWhiteSpace(targetEmail))
                                targetEmail = r.IsDBNull(r.GetOrdinal("TargetEmail")) ? "" : r.GetString(r.GetOrdinal("TargetEmail"));
                            customerName = r.IsDBNull(r.GetOrdinal("CustomerName")) ? "Valued Customer" : r.GetString(r.GetOrdinal("CustomerName"));
                            spaceName = r.IsDBNull(r.GetOrdinal("SpaceName")) ? "WorkNest Workspace" : r.GetString(r.GetOrdinal("SpaceName"));
                            grandTotal = r.GetDecimal(r.GetOrdinal("GrandTotal"));
                            subTotal = r.GetDecimal(r.GetOrdinal("SubTotal"));
                            taxTotal = r.GetDecimal(r.GetOrdinal("TaxTotal"));
                            discountTotal = r.GetDecimal(r.GetOrdinal("DiscountTotal"));
                            issuedOn = r.GetDateTime(r.GetOrdinal("IssuedOn"));
                            dueOn = r.GetDateTime(r.GetOrdinal("DueOn"));
                        }
                    }

                    if (!invoiceFound || string.IsNullOrWhiteSpace(targetEmail))
                    {
                        throw new InvalidOperationException($"Invoice {item.InvoiceId} data or target email missing.");
                    }

                    byte[]? pdfBytes = null;
                    try
                    {
                        var pdfDto = await BuildPdfDtoAsync(item.InvoiceId, conn);
                        if (pdfDto != null)
                        {
                            pdfBytes = StatementInvoicePdfGenerator.GeneratePdf(pdfDto);
                        }
                    }
                    catch (Exception pdfEx)
                    {
                        _logger.LogWarning(pdfEx, "Failed to render PDF for retry of Invoice ID {InvoiceId}.", item.InvoiceId);
                    }

                    await emailService.SendChallanEmailAsync(
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

                    // Success update
                    string successSql = @"
                        UPDATE dbo.WN_InvoiceDeliveryQueue 
                        SET Status = 'Sent', Attempts = @Attempts, LastAttemptAt = SYSUTCDATETIME(), NextRetryAt = NULL, LastError = NULL 
                        WHERE Id = @Id;";
                    using var okCmd = new SqlCommand(successSql, conn);
                    okCmd.Parameters.AddWithValue("@Attempts", attemptNum);
                    okCmd.Parameters.AddWithValue("@Id", item.Id);
                    await okCmd.ExecuteNonQueryAsync(stoppingToken);

                    _logger.LogInformation("Successfully delivered email for Invoice ID {InvoiceId} on retry attempt {Attempt}.", item.InvoiceId, attemptNum);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Retry attempt {Attempt} failed for Invoice ID {InvoiceId}.", attemptNum, item.InvoiceId);

                    string nextStatus = attemptNum >= 3 ? "FailedNeedsAttention" : "Pending";
                    int nextMinutes = attemptNum == 1 ? 5 : (attemptNum == 2 ? 15 : 60);

                    if (attemptNum >= 3)
                    {
                        _logger.LogError("[HUMAN ALERT] Invoice delivery for Invoice ID {InvoiceId} to {Email} failed 3 consecutive times. Status set to FailedNeedsAttention.", item.InvoiceId, item.TargetEmail);
                    }

                    string failSql = @"
                        UPDATE dbo.WN_InvoiceDeliveryQueue 
                        SET Attempts = @Attempts, 
                            LastAttemptAt = SYSUTCDATETIME(), 
                            NextRetryAt = CASE WHEN @NextStatus = 'Pending' THEN DATEADD(minute, @NextMinutes, SYSUTCDATETIME()) ELSE NULL END, 
                            Status = @NextStatus, 
                            LastError = @LastError 
                        WHERE Id = @Id;";

                    using var failCmd = new SqlCommand(failSql, conn);
                    failCmd.Parameters.AddWithValue("@Attempts", attemptNum);
                    failCmd.Parameters.AddWithValue("@NextMinutes", nextMinutes);
                    failCmd.Parameters.AddWithValue("@NextStatus", nextStatus);
                    failCmd.Parameters.AddWithValue("@LastError", ex.Message);
                    failCmd.Parameters.AddWithValue("@Id", item.Id);
                    await failCmd.ExecuteNonQueryAsync(stoppingToken);
                }
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

        private async Task<StatementInvoicePdfDto?> BuildPdfDtoAsync(int id, SqlConnection conn)
        {
            var dto = new StatementInvoicePdfDto();
            using var cmd = new SqlCommand("dbo.WN_GetStatementInvoicePdfData", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@InvoiceId", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                dto.InvoiceNumber = reader.GetString(reader.GetOrdinal("InvoiceNumber"));
                dto.InvoiceDate = reader.GetDateTime(reader.GetOrdinal("IssuedOn"));
                dto.DueDate = reader.GetDateTime(reader.GetOrdinal("DueOn"));
                dto.BillingPeriodStart = HasColumn(reader, "BillingPeriodStart") && !reader.IsDBNull(reader.GetOrdinal("BillingPeriodStart")) ? reader.GetDateTime(reader.GetOrdinal("BillingPeriodStart")) : (DateTime?)null;
                dto.BillingPeriodEnd = HasColumn(reader, "BillingPeriodEnd") && !reader.IsDBNull(reader.GetOrdinal("BillingPeriodEnd")) ? reader.GetDateTime(reader.GetOrdinal("BillingPeriodEnd")) : (DateTime?)null;
                dto.CurrentInvoiceTotal = reader.GetDecimal(reader.GetOrdinal("GrandTotal"));
                // Same fields as InvoiceController.BuildStatementInvoicePdfDtoAsync, so emailed PDFs match the
                // downloaded ones (PST total, WHT invoices without the "if tax is withheld" terms).
                if (HasColumn(reader, "SubTotal") && !reader.IsDBNull(reader.GetOrdinal("SubTotal")))
                    dto.SubTotal = reader.GetDecimal(reader.GetOrdinal("SubTotal"));
                if (HasColumn(reader, "DiscountTotal") && !reader.IsDBNull(reader.GetOrdinal("DiscountTotal")))
                    dto.DiscountTotal = reader.GetDecimal(reader.GetOrdinal("DiscountTotal"));
                if (HasColumn(reader, "TaxTotal") && !reader.IsDBNull(reader.GetOrdinal("TaxTotal")))
                    dto.TaxTotal = reader.GetDecimal(reader.GetOrdinal("TaxTotal"));
                if (HasColumn(reader, "WithholdingTaxRate") && !reader.IsDBNull(reader.GetOrdinal("WithholdingTaxRate")))
                    dto.WithholdingTaxRate = reader.GetDecimal(reader.GetOrdinal("WithholdingTaxRate"));
                dto.IsWhtInvoice = HasColumn(reader, "InvoiceTypeId") && !reader.IsDBNull(reader.GetOrdinal("InvoiceTypeId"))
                    && Convert.ToInt32(reader.GetValue(reader.GetOrdinal("InvoiceTypeId"))) == 6;
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

                    items.Add(new StatementInvoiceLineItemDto
                    {
                        Description = desc,
                        FromDate = lineFrom,
                        ToDate = lineTo,
                        PriceExclVat = reader.IsDBNull(reader.GetOrdinal("PriceExclVat")) ? 0m : reader.GetDecimal(reader.GetOrdinal("PriceExclVat")),
                        VatAmount = reader.IsDBNull(reader.GetOrdinal("VatAmount")) ? 0m : reader.GetDecimal(reader.GetOrdinal("VatAmount")),
                        Category = HasColumn(reader, "CategoryName") && !reader.IsDBNull(reader.GetOrdinal("CategoryName")) ? reader.GetString(reader.GetOrdinal("CategoryName")) : "Recurring",
                        IsDeposit = isDeposit
                    });
                }
                if (!items.Any(i => i.IsDeposit || i.Description.Contains("Deposit", StringComparison.OrdinalIgnoreCase)) && dto.SecurityDepositAmount > 0)
                {
                    items.Add(new StatementInvoiceLineItemDto
                    {
                        Description = "Security Deposit (Refundable)",
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

            return dto;
        }

        private class QueueItemDto
        {
            public int Id { get; set; }
            public int InvoiceId { get; set; }
            public int? BookingId { get; set; }
            public string? TargetEmail { get; set; }
            public int Attempts { get; set; }
        }
    }
}
