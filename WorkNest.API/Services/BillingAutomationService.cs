using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Background Service that automatically generates recurring invoices 5 days before
    /// each active booking's current billing cycle ends.
    /// Ensures idempotency (prevents duplicate invoices) and charges security deposit ONLY ONCE.
    /// </summary>
    public class BillingAutomationService : BackgroundService
    {
        private readonly ILogger<BillingAutomationService> _logger;
        private readonly string _connectionString;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1); // Runs hourly

        public BillingAutomationService(ILogger<BillingAutomationService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("DefaultConnection string is missing.");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WorkNest Billing Automation Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessRecurringBillingAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during automatic billing processing cycle.");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("WorkNest Billing Automation Service stopping.");
        }

        private async Task ProcessRecurringBillingAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Checking for active bookings due for recurring invoice generation...");

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(stoppingToken);
            using var setCmd = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conn);
            await setCmd.ExecuteNonQueryAsync(stoppingToken);

            // Step 1: Fetch active bookings due for next billing cycle (within 5 days of cycle end)
            var dueBookings = new List<DueBookingDto>();

            using (var cmd = new SqlCommand("dbo.WN_Billing_GetDueBookings", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                using var reader = await cmd.ExecuteReaderAsync(stoppingToken);
                while (await reader.ReadAsync(stoppingToken))
                {
                    dueBookings.Add(new DueBookingDto
                    {
                        BookingId = reader.GetInt32(reader.GetOrdinal("BookingId")),
                        ContractStart = reader.GetDateTime(reader.GetOrdinal("ContractStart")),
                        ContractEnd = reader.GetDateTime(reader.GetOrdinal("ContractEnd")),
                        MonthlyRent = reader.GetDecimal(reader.GetOrdinal("MonthlyRent")),
                        BillingPeriodMonths = reader.GetInt32(reader.GetOrdinal("BillingPeriodMonths")),
                        SecurityDepositCharged = reader.GetBoolean(reader.GetOrdinal("SecurityDepositCharged")),
                        CurrentPeriodStart = reader.IsDBNull(reader.GetOrdinal("CurrentPeriodStart")) ? null : reader.GetDateTime(reader.GetOrdinal("CurrentPeriodStart")),
                        CurrentPeriodEnd = reader.IsDBNull(reader.GetOrdinal("CurrentPeriodEnd")) ? null : reader.GetDateTime(reader.GetOrdinal("CurrentPeriodEnd")),
                        NextBillingDate = reader.IsDBNull(reader.GetOrdinal("NextBillingDate")) ? null : reader.GetDateTime(reader.GetOrdinal("NextBillingDate")),
                    });
                }
            }

            _logger.LogInformation("Found {Count} active booking(s) due for billing processing.", dueBookings.Count);

            foreach (var b in dueBookings)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    // Calculate Next Billing Period
                    DateTime nextPeriodStart = b.NextBillingDate ?? (b.CurrentPeriodEnd?.AddDays(1) ?? b.ContractStart);
                    
                    // Cap at Contract End Date
                    if (nextPeriodStart >= b.ContractEnd)
                    {
                        _logger.LogInformation("Booking {BookingId} has reached or exceeded contract end date {ContractEnd}. Skipping invoice creation.", b.BookingId, b.ContractEnd);
                        continue;
                    }

                    // Anchored to the contract start so month-end starts don't drift (see BillingPeriods).
                    DateTime rawPeriodEnd = WorkNest.Application.Services.BillingPeriods.PeriodEnd(b.ContractStart, nextPeriodStart, b.BillingPeriodMonths);
                    DateTime nextPeriodEnd = rawPeriodEnd > b.ContractEnd ? b.ContractEnd : rawPeriodEnd;

                    // One instance at a time per booking (IIS overlapped recycle, a second server): the check below and
                    // the insert run under a session lock, and an instance that can't get it skips this booking.
                    if (!await TryLockBookingAsync(conn, b.BookingId, stoppingToken))
                    {
                        _logger.LogInformation("Booking {BookingId} is being billed by another instance. Skipping.", b.BookingId);
                        continue;
                    }
                    try
                    {
                    // Step 2: Idempotent Duplicate Invoice Check
                    bool invoiceExists = false;
                    using (var checkCmd = new SqlCommand("dbo.WN_Invoice_CheckExistingBillingPeriod", conn))
                    {
                        checkCmd.CommandType = CommandType.StoredProcedure;
                        checkCmd.Parameters.AddWithValue("@BookingId", b.BookingId);
                        checkCmd.Parameters.AddWithValue("@BillingPeriodStart", nextPeriodStart);
                        checkCmd.Parameters.AddWithValue("@BillingPeriodEnd", nextPeriodEnd);

                        var res = await checkCmd.ExecuteScalarAsync(stoppingToken);
                        invoiceExists = Convert.ToInt32(res) == 1;
                    }

                    if (invoiceExists)
                    {
                        _logger.LogInformation("Invoice already exists for Booking {BookingId} period [{Start:yyyy-MM-dd} to {End:yyyy-MM-dd}]. Skipping duplicate generation.", b.BookingId, nextPeriodStart, nextPeriodEnd);
                        continue;
                    }

                    // Step 3: Generate Recurring Invoice via SP
                    using (var genCmd = new SqlCommand("dbo.WN_Invoice_CreateRecurring", conn))
                    {
                        genCmd.CommandType = CommandType.StoredProcedure;
                        genCmd.Parameters.AddWithValue("@BookingId", b.BookingId);
                        genCmd.Parameters.AddWithValue("@BillingPeriodStart", nextPeriodStart);
                        genCmd.Parameters.AddWithValue("@BillingPeriodEnd", nextPeriodEnd);

                        using var genReader = await genCmd.ExecuteReaderAsync(stoppingToken);
                        if (await genReader.ReadAsync(stoppingToken))
                        {
                            var invoiceId = genReader.GetInt32(genReader.GetOrdinal("InvoiceId"));
                            var invoiceNo = genReader.GetString(genReader.GetOrdinal("InvoiceNumber"));
                            var total = genReader.GetDecimal(genReader.GetOrdinal("TotalAmount"));

                            _logger.LogInformation("Successfully generated recurring invoice {InvoiceNo} (ID: {InvoiceId}) for Booking {BookingId}. Total: PKR {Total}.", invoiceNo, invoiceId, b.BookingId, total);
                        }
                    }
                    }
                    finally
                    {
                        await UnlockBookingAsync(conn, b.BookingId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process recurring invoice generation for Booking {BookingId}.", b.BookingId);
                }
            }
        }

        private static async Task<bool> TryLockBookingAsync(SqlConnection conn, int bookingId, CancellationToken ct)
        {
            using var cmd = new SqlCommand(@"
                DECLARE @r INT;
                EXEC @r = sp_getapplock @Resource = @Res, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
                SELECT @r;", conn);
            cmd.Parameters.AddWithValue("@Res", $"wn-billing-{bookingId}");
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) >= 0; // 0 / 1 = granted, negative = held elsewhere
        }

        private static async Task UnlockBookingAsync(SqlConnection conn, int bookingId)
        {
            try
            {
                using var cmd = new SqlCommand("EXEC sp_releaseapplock @Resource = @Res, @LockOwner = 'Session';", conn);
                cmd.Parameters.AddWithValue("@Res", $"wn-billing-{bookingId}");
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* the lock also ends when the connection closes */ }
        }

        private class DueBookingDto
        {
            public int BookingId { get; set; }
            public DateTime ContractStart { get; set; }
            public DateTime ContractEnd { get; set; }
            public decimal MonthlyRent { get; set; }
            public int BillingPeriodMonths { get; set; }
            public bool SecurityDepositCharged { get; set; }
            public DateTime? CurrentPeriodStart { get; set; }
            public DateTime? CurrentPeriodEnd { get; set; }
            public DateTime? NextBillingDate { get; set; }
        }
    }
}
