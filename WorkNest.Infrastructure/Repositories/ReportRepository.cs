using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Reports;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Data for the revenue &amp; occupancy forecast and the weekly super admin report (reads with NOLOCK, same status
    /// rules as the dashboards: live bookings 1, 2, 5, 33; running leases 2, 33; active spaces Status = 1), and the
    /// run log WN_ScheduledReportRuns that stops the weekly e-mail from going out twice.
    /// </summary>
    public class ReportRepository : IReportRepository
    {
        private readonly string _connectionString;

        public ReportRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private static string IdList(IEnumerable<int> ids)
        {
            var l = ids.Distinct().ToList();
            return l.Count == 0 ? "-1" : string.Join(",", l);   // ints from code, never user text
        }

        private async Task<List<List<IDictionary<string, object?>>>> QueryAsync(string sql, Action<SqlParameterCollection> bind)
        {
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 60 };
            bind(cmd.Parameters);
            await using var r = await cmd.ExecuteReaderAsync();
            var sets = new List<List<IDictionary<string, object?>>>();
            do
            {
                var rows = new List<IDictionary<string, object?>>();
                while (await r.ReadAsync())
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                    rows.Add(row);
                }
                sets.Add(rows);
            } while (await r.NextResultAsync());
            return sets;
        }

        private const string CustomerName = @"COALESCE(NULLIF(cu.Company, ''), NULLIF(LTRIM(RTRIM(CONCAT(cu.FirstName, ' ', cu.LastName))), ''), cu.Email)";

        public Task<List<List<IDictionary<string, object?>>>> GetForecastAsync(IReadOnlyCollection<int>? locationIds, DateTime firstMonth, int months) =>
            QueryAsync($@"
                DECLARE @First DATE = @FirstMonth;
                DECLARE @Last DATE = DATEADD(MONTH, @Months, @First);   -- exclusive

                DECLARE @Spaces TABLE (Id INT PRIMARY KEY, LocationId INT NOT NULL);
                INSERT INTO @Spaces (Id, LocationId)
                SELECT s.Id, ISNULL(s.LocationId, 0)
                  FROM dbo.WN_Spaces s WITH (NOLOCK)
                 WHERE s.Status = 1 {(locationIds is null ? "" : $"AND s.LocationId IN ({IdList(locationIds)})")};

                -- Live leases that overlap the forecast months. Rent = the booking's monthly rent less a percentage
                -- discount (a fixed-amount discount is per billing cycle, not per month, so it is left out); no tax
                -- or service charge.
                DECLARE @Live TABLE (Id INT PRIMARY KEY, SpaceId INT, LocationId INT, StartOn DATE, EndOn DATE, StatusId INT, Rent DECIMAL(18,2));
                INSERT INTO @Live
                SELECT b.Id, b.SpaceId, sp.LocationId, CAST(b.StartOn AS DATE), CAST(b.EndOn AS DATE), b.BookingStatusId,
                       ISNULL(b.MonthlyRent, 0) * (100 - d.Pct) / 100
                  FROM dbo.WN_Bookings b WITH (NOLOCK)
                  JOIN @Spaces sp ON sp.Id = b.SpaceId
                 -- Only DiscountPercentage (used by WN_Invoice_CreateRecurring); fixed-amount discounts are per cycle.
                 CROSS APPLY (SELECT CASE WHEN ISNULL(b.DiscountPercentage, 0) BETWEEN 0.01 AND 100 THEN b.DiscountPercentage
                                          ELSE 0 END AS Pct) d
                 WHERE ISNULL(b.IsDeleted, 0) = 0 AND b.BookingStatusId IN (1, 2, 5, 33)
                   AND b.StartOn < @Last AND b.EndOn >= @First;

                ;WITH m AS (
                    SELECT n, DATEADD(MONTH, n, @First) AS MStart, EOMONTH(DATEADD(MONTH, n, @First)) AS MEnd
                      FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11)) v(n)
                     WHERE n < @Months
                ), loc AS (
                    SELECT LocationId, COUNT(*) AS TotalSpaces FROM @Spaces GROUP BY LocationId
                )
                -- [0] per location and month. Occupancy is measured on the month's last day (as the overview trend).
                SELECT m.n, CONVERT(VARCHAR(7), m.MStart, 126) AS Month, loc.LocationId, l.Name AS LocationName, loc.TotalSpaces,
                       (SELECT COUNT(DISTINCT x.SpaceId) FROM @Live x
                         WHERE x.LocationId = loc.LocationId AND x.StartOn <= m.MEnd AND x.EndOn >= m.MEnd) AS OccupiedSpaces,
                       ISNULL(r.ExpectedRent, 0) AS ExpectedRent, ISNULL(r.ConfirmedRent, 0) AS ConfirmedRent,
                       (SELECT COUNT(*) FROM @Live x
                         WHERE x.LocationId = loc.LocationId AND x.StatusId IN (2, 33) AND x.EndOn BETWEEN m.MStart AND m.MEnd) AS LeasesEnding,
                       (SELECT COUNT(*) FROM @Live x
                         WHERE x.LocationId = loc.LocationId AND x.StartOn BETWEEN m.MStart AND m.MEnd) AS LeasesStarting,
                       (SELECT COUNT(*) FROM @Live x
                         WHERE x.LocationId = loc.LocationId AND x.StatusId IN (1, 5) AND x.StartOn BETWEEN m.MStart AND m.MEnd) AS PendingStarting
                  FROM m
                 CROSS JOIN loc
                  LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = loc.LocationId
                 OUTER APPLY (
                       -- 30-day month (InvoiceCalculationEngine.ProrationDaysPerMonth): a lease covering the whole month
                       -- counts 30 days; otherwise its first / last day in the month, with the 31st counted as the 30th.
                       SELECT SUM(x.Rent * dd.Days / 30) AS ExpectedRent,
                              SUM(CASE WHEN x.StatusId IN (2, 33) THEN x.Rent * dd.Days / 30 ELSE 0 END) AS ConfirmedRent
                         FROM @Live x
                        CROSS APPLY (SELECT CASE WHEN x.StartOn <= m.MStart THEN 1 WHEN DAY(x.StartOn) > 30 THEN 30 ELSE DAY(x.StartOn) END AS FromDay,
                                            CASE WHEN x.EndOn >= m.MEnd THEN 30 WHEN DAY(x.EndOn) > 30 THEN 30 ELSE DAY(x.EndOn) END AS ToDay) fd
                        CROSS APPLY (SELECT CASE WHEN fd.ToDay >= fd.FromDay THEN fd.ToDay - fd.FromDay + 1 ELSE 0 END AS Days) dd
                        WHERE x.LocationId = loc.LocationId AND x.StartOn <= m.MEnd AND x.EndOn >= m.MStart) r
                 ORDER BY m.n, l.Name;",
                p =>
                {
                    p.Add("@FirstMonth", SqlDbType.Date).Value = firstMonth.Date;
                    p.Add("@Months", SqlDbType.Int).Value = months;
                });

        public Task<List<List<IDictionary<string, object?>>>> GetWeeklyExtrasAsync(DateTime weekStart, DateTime now) =>
            QueryAsync($@"
                DECLARE @Now DATETIME2(0) = @BizNow;   -- business (Pakistan) time from the app clock
                DECLARE @In30 DATETIME2(0) = DATEADD(DAY, 30, @Now), @In60 DATETIME2(0) = DATEADD(DAY, 60, @Now);

                -- [0] tour inquiries received in the last 7 days (not tied to a location)
                SELECT COUNT(*) AS NewInquiries FROM dbo.WN_Contacts WITH (NOLOCK) WHERE CreatedOn >= @WeekStart;

                -- [1] running leases expiring in the next 30 / 60 days, per location
                SELECT s.LocationId,
                       SUM(CASE WHEN b.EndOn <= @In30 THEN 1 ELSE 0 END) AS Expiring30,
                       COUNT(*) AS Expiring60
                  FROM dbo.WN_Bookings b WITH (NOLOCK)
                  JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                 WHERE s.Status = 1 AND ISNULL(b.IsDeleted, 0) = 0 AND b.BookingStatusId IN (2, 33)
                   AND b.EndOn >= @Now AND b.EndOn <= @In60
                 GROUP BY s.LocationId;

                -- [2] the first 10 of them
                SELECT TOP 10 b.Id AS BookingId, b.EndOn, b.MonthlyRent,
                       COALESCE(NULLIF(s.Name, ''), s.Code) AS Space, l.Name AS Location, {CustomerName} AS Customer
                  FROM dbo.WN_Bookings b WITH (NOLOCK)
                  JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                  LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = s.LocationId
                 OUTER APPLY (SELECT TOP 1 * FROM dbo.WN_Customers c2 WITH (NOLOCK) WHERE c2.Code = b.CustomerCode OR c2.UserId = b.UserId) cu
                 WHERE s.Status = 1 AND ISNULL(b.IsDeleted, 0) = 0 AND b.BookingStatusId IN (2, 33)
                   AND b.EndOn >= @Now AND b.EndOn <= @In60
                 ORDER BY b.EndOn, b.Id;",
                p =>
                {
                    p.Add("@WeekStart", SqlDbType.DateTime2).Value = weekStart;
                    p.Add("@BizNow", SqlDbType.DateTime2).Value = now;
                });

        public async Task<List<string>> GetSuperAdminEmailsAsync()
        {
            var sets = await QueryAsync(@"
                SELECT DISTINCT LTRIM(RTRIM(Email)) AS Email
                  FROM dbo.WN_Users WITH (NOLOCK)
                 WHERE RoleId = 1 AND IsActive = 1 AND Email LIKE '%_@_%._%';",
                _ => { });
            return sets.Count == 0 ? new List<string>()
                : sets[0].Select(r => r.TryGetValue("Email", out var v) ? Convert.ToString(v) : null)
                         .Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e!).ToList();
        }

        public async Task<ReportClaimResult> TryClaimRunAsync(string reportKey, string periodKey, DateTime at, DateTime retryFailedBefore)
        {
            // The UNIQUE (ReportKey, PeriodKey) key is what makes this safe across restarts and several IIS instances:
            // the first instance to insert the row sends the report; the others get a key violation and skip.
            // A run that failed (nothing was sent) may be claimed again once retryFailedBefore has passed.
            const string sql = @"
                SET XACT_ABORT ON;
                BEGIN TRAN;
                DECLARE @Status NVARCHAR(20), @At0 DATETIME2(0);
                SELECT @Status = Status, @At0 = SentAt
                  FROM dbo.WN_ScheduledReportRuns WITH (UPDLOCK, HOLDLOCK)
                 WHERE ReportKey = @Key AND PeriodKey = @Period;
                IF @Status IS NULL
                BEGIN
                    INSERT INTO dbo.WN_ScheduledReportRuns (ReportKey, PeriodKey, SentAt, Recipients, Status, Error)
                    VALUES (@Key, @Period, @At, NULL, N'Sending', NULL);
                    SELECT 'Claimed' AS Result;
                END
                ELSE IF @Status = N'Failed' AND @At0 < @RetryBefore
                BEGIN
                    UPDATE dbo.WN_ScheduledReportRuns SET Status = N'Sending', SentAt = @At, Error = NULL
                     WHERE ReportKey = @Key AND PeriodKey = @Period;
                    SELECT 'Claimed' AS Result;
                END
                ELSE
                    SELECT CASE WHEN @Status = N'Failed' THEN 'RetryLater' ELSE 'Taken' END AS Result;
                COMMIT;";
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 30 };
                cmd.Parameters.Add("@Key", SqlDbType.NVarChar, 50).Value = reportKey;
                cmd.Parameters.Add("@Period", SqlDbType.NVarChar, 20).Value = periodKey;
                cmd.Parameters.Add("@At", SqlDbType.DateTime2).Value = at;
                cmd.Parameters.Add("@RetryBefore", SqlDbType.DateTime2).Value = retryFailedBefore;
                var result = Convert.ToString(await cmd.ExecuteScalarAsync());
                return result switch
                {
                    "Claimed" => ReportClaimResult.Claimed,
                    "RetryLater" => ReportClaimResult.RetryLater,
                    _ => ReportClaimResult.Taken
                };
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601) { return ReportClaimResult.Taken; }   // another instance inserted it first
            catch (SqlException ex) when (ex.Number == 208) { return ReportClaimResult.TableMissing; }    // table not created yet
        }

        public async Task CompleteRunAsync(string reportKey, string periodKey, string status, string recipients, string? error, DateTime at)
        {
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            await using var cmd = new SqlCommand(@"
                UPDATE dbo.WN_ScheduledReportRuns
                   SET Status = @Status, Recipients = @Recipients, Error = @Error, SentAt = @At
                 WHERE ReportKey = @Key AND PeriodKey = @Period;", c) { CommandTimeout = 30 };
            cmd.Parameters.Add("@Key", SqlDbType.NVarChar, 50).Value = reportKey;
            cmd.Parameters.Add("@Period", SqlDbType.NVarChar, 20).Value = periodKey;
            cmd.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = status;
            cmd.Parameters.Add("@Recipients", SqlDbType.NVarChar, -1).Value = recipients;
            cmd.Parameters.Add("@Error", SqlDbType.NVarChar, 1000).Value = error is null ? DBNull.Value : (error.Length > 1000 ? error[..1000] : error);
            cmd.Parameters.Add("@At", SqlDbType.DateTime2).Value = at;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
