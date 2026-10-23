using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

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
        private readonly ServiceSwitchGate _gate; // on/off switch (api/admin/services): off skips the cycle's work

        private readonly IServiceProvider _serviceProvider;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _remindersSentToday = new();

        public BillingAutomationService(ILogger<BillingAutomationService> logger, IConfiguration configuration, IServiceSwitch serviceSwitch, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _gate = new ServiceSwitchGate(serviceSwitch, BackgroundServiceCatalog.BillingAutomation, logger);
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("DefaultConnection string is missing.");
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WorkNest Billing Automation Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (await _gate.IsOnAsync(stoppingToken))
                    {
                        await ProcessRecurringBillingAsync(stoppingToken);
                        await CheckAndSendPendingApprovalRemindersAsync(stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during automatic billing processing cycle.");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("WorkNest Billing Automation Service stopping.");
        }

        private async Task CheckAndSendPendingApprovalRemindersAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var clock = scope.ServiceProvider.GetRequiredService<IBusinessClock>();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                var today = clock.Today;

                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(stoppingToken);

                int deadlineDay = 18;
                int reminderDaysBefore = 3;

                using (var setCmd = new SqlCommand("SELECT SettingKey, SettingValue FROM dbo.WN_BillingSettings WITH (NOLOCK) WHERE SettingKey IN ('SendDeadlineDay', 'ReminderDaysBeforeDeadline')", conn))
                using (var r = await setCmd.ExecuteReaderAsync(stoppingToken))
                {
                    while (await r.ReadAsync(stoppingToken))
                    {
                        var key = r.GetString(0);
                        var val = r.GetString(1);
                        if (key == "SendDeadlineDay" && int.TryParse(val, out var d)) deadlineDay = d;
                        if (key == "ReminderDaysBeforeDeadline" && int.TryParse(val, out var rm)) reminderDaysBefore = rm;
                    }
                }

                int reminderStartDay = Math.Max(1, deadlineDay - reminderDaysBefore);
                if (today.Day < reminderStartDay || today.Day > deadlineDay)
                {
                    return; // Outside reminder window
                }

                // Find PendingApproval invoices grouped by location
                const string query = @"
                    SELECT 
                        ISNULL(s.LocationId, u.LocationId) AS LocationId,
                        ISNULL(loc.Name, 'Location') AS LocationName,
                        COUNT(i.Id) AS PendingCount,
                        SUM(ISNULL(i.GrandTotal, 0)) AS PendingTotal
                    FROM dbo.WN_Invoices i WITH (NOLOCK)
                    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
                    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
                    LEFT JOIN dbo.WN_Locations loc WITH (NOLOCK) ON loc.Id = ISNULL(s.LocationId, u.LocationId)
                    WHERE i.ApprovalStatus = 'PendingApproval'
                      AND i.StatusId <> 40
                      AND ISNULL(s.LocationId, u.LocationId) IS NOT NULL
                    GROUP BY ISNULL(s.LocationId, u.LocationId), loc.Name;";

                var locationSummaries = new List<(int LocationId, string LocationName, int Count, decimal Total)>();

                using (var cmd = new SqlCommand(query, conn))
                using (var r = await cmd.ExecuteReaderAsync(stoppingToken))
                {
                    while (await r.ReadAsync(stoppingToken))
                    {
                        locationSummaries.Add((
                            LocationId: r.GetInt32(0),
                            LocationName: r.GetString(1),
                            Count: r.GetInt32(2),
                            Total: r.GetDecimal(3)
                        ));
                    }
                }

                foreach (var loc in locationSummaries)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    string key = $"{loc.LocationId}_{today:yyyyMMdd}";
                    if (_remindersSentToday.ContainsKey(key)) continue;

                    // Query sales executives (RoleId 16) for this location
                    var recipients = new List<(string Email, string Name)>();
                    using (var userCmd = new SqlCommand(@"
                        SELECT Email, Name 
                        FROM dbo.WN_Users WITH (NOLOCK) 
                        WHERE (RoleId = 16 OR RoleId = 1) 
                          AND IsActive = 1 
                          AND LocationId = @LocationId 
                          AND Email IS NOT NULL AND LTRIM(RTRIM(Email)) <> '';", conn))
                    {
                        userCmd.Parameters.AddWithValue("@LocationId", loc.LocationId);
                        using var ur = await userCmd.ExecuteReaderAsync(stoppingToken);
                        while (await ur.ReadAsync(stoppingToken))
                        {
                            recipients.Add((ur.GetString(0), ur.IsDBNull(1) ? "Team Member" : ur.GetString(1)));
                        }
                    }

                    if (recipients.Count == 0) continue;

                    foreach (var (email, name) in recipients)
                    {
                        try
                        {
                            var html = $@"
                                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 8px;'>
                                    <h2 style='color: #1e293b; margin-top: 0;'>Pending Recurring Invoices Action Required</h2>
                                    <p>Dear {name},</p>
                                    <p>There are currently <strong>{loc.Count}</strong> recurring invoice(s) totaling <strong>PKR {loc.Total:N0}</strong> pending your review and approval for <strong>{loc.LocationName}</strong>.</p>
                                    <p style='color: #b91c1c; font-weight: bold;'>Approval deadline: {deadlineDay}th of this month. Invoices must be inspected and approved individually before delivery.</p>
                                    <p>Please log in to the WorkNest portal and open the approval queue:</p>
                                    <div style='margin: 25px 0;'>
                                        <a href='https://worknest.vercel.app/admin/invoice-approvals' style='background: #2563eb; color: #ffffff; padding: 12px 24px; border-radius: 6px; text-decoration: none; font-weight: bold; display: inline-block;'>Open Approval Queue</a>
                                    </div>
                                    <p style='font-size: 13px; color: #64748b;'>Note: Invoices are never sent automatically. Each invoice requires personal inspection before approval.</p>
                                </div>";

                            await emailService.SendHtmlReportAsync(email, $"[WorkNest Action Required] {loc.Count} Pending Invoices for {loc.LocationName}", html);
                            _logger.LogInformation("Sent pending invoice reminder to {Email} for {LocationName} ({Count} invoices).", email, loc.LocationName, loc.Count);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to send pending invoice reminder to {Email}.", email);
                        }
                    }

                    _remindersSentToday.TryAdd(key, today);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in pending approval reminders check.");
            }
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

                    // All subsequent recurring cycles after initial advance rent are billed monthly (1 month at a time).
                    const int recurringBillingMonths = 1;
                    DateTime rawPeriodEnd = WorkNest.Application.Services.BillingPeriods.PeriodEnd(b.ContractStart, nextPeriodStart, recurringBillingMonths);
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
                        int invIdOrd = -1, numOrd = -1, totOrd = -1;
                        do
                        {
                            if (await genReader.ReadAsync(stoppingToken))
                            {
                                try { invIdOrd = genReader.GetOrdinal("InvoiceId"); } catch { }
                                try { numOrd = genReader.GetOrdinal("InvoiceNumber"); } catch { }
                                try { totOrd = genReader.GetOrdinal("TotalAmount"); } catch { }

                                if (invIdOrd >= 0 && !genReader.IsDBNull(invIdOrd))
                                {
                                    if (totOrd >= 0) break;
                                }
                            }
                        } while (await genReader.NextResultAsync(stoppingToken));

                        if (invIdOrd >= 0 && !genReader.IsDBNull(invIdOrd))
                        {
                            var invoiceId = genReader.GetInt32(invIdOrd);
                            var invoiceNo = numOrd >= 0 && !genReader.IsDBNull(numOrd) ? genReader.GetString(numOrd) : "";
                            var total = totOrd >= 0 && !genReader.IsDBNull(totOrd) ? genReader.GetDecimal(totOrd) : 0m;

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
