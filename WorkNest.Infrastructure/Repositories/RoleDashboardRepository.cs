using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Data for the role-based dashboard sections (sales pipeline, team &amp; service, location comparison, staff,
    /// system). Reads only, with NOLOCK. Tables that may not exist yet (complaints, WhatsApp, inquiry feedback, KYC,
    /// e-mail queue) are checked with OBJECT_ID first, so a missing table gives zeros instead of an error.
    /// </summary>
    public class RoleDashboardRepository : IRoleDashboardRepository
    {
        private readonly string _connectionString;

        public RoleDashboardRepository(IConfiguration configuration)
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

        /// <summary>Spaces in scope: all active spaces, or only those in the given locations.</summary>
        private static string SpacesSql(IReadOnlyCollection<int>? locationIds) => $@"
            DECLARE @Spaces TABLE (Id INT PRIMARY KEY, Name NVARCHAR(200), LocationId INT NULL);
            INSERT INTO @Spaces (Id, Name, LocationId)
            SELECT s.Id, COALESCE(NULLIF(s.Name, ''), s.Code), s.LocationId
              FROM dbo.WN_Spaces s WITH (NOLOCK)
             WHERE s.Status = 1 {(locationIds is null ? "" : $"AND s.LocationId IN ({IdList(locationIds)})")};";

        private const string CustomerName = @"COALESCE(NULLIF(cu.Company, ''), NULLIF(LTRIM(RTRIM(CONCAT(cu.FirstName, ' ', cu.LastName))), ''), cu.Email)";

        public Task<List<List<IDictionary<string, object?>>>> GetSalesAsync(IReadOnlyCollection<int>? locationIds,
            DateTime periodStart, DateTime now, IEnumerable<int> openInvoiceStatusIds, int renewalDays) =>
            QueryAsync($@"
                {SpacesSql(locationIds)}
                DECLARE @Now DATETIME2(0) = @BizNow;   -- business (Pakistan) time from the app clock

                -- [0] quotation pipeline: quotations created in the period, by where they are now
                SELECT COUNT(*) AS Created,
                       SUM(CASE WHEN q.Status IN ('Draft') THEN 1 ELSE 0 END) AS Draft,
                       SUM(CASE WHEN q.Status IN ('Sent') THEN 1 ELSE 0 END) AS Sent,
                       SUM(CASE WHEN q.Status IN ('Accepted') THEN 1 ELSE 0 END) AS Accepted,
                       SUM(CASE WHEN q.Status IN ('AgreementSent', 'Signed') THEN 1 ELSE 0 END) AS AgreementStage,
                       SUM(CASE WHEN q.Status IN ('Converted') THEN 1 ELSE 0 END) AS Converted,
                       SUM(CASE WHEN q.Status IN ('Declined', 'Rejected', 'Expired', 'Cancelled') THEN 1 ELSE 0 END) AS Lost,
                       ISNULL(SUM(CASE WHEN q.Status IN ('Converted') THEN q.TotalAmount ELSE 0 END), 0) AS ConvertedValue
                  FROM dbo.WN_Quotations q WITH (NOLOCK)
                  JOIN @Spaces sp ON sp.Id = q.SpaceId
                 WHERE ISNULL(q.IsActive, 1) = 1 AND q.CreatedDate >= @PStart;

                -- [1] agreements sent but not signed yet (newest agreement per quotation)
                SELECT TOP 8 a.Id, q.QuotationNumber AS Reference, a.SentDate AS [Date], q.TotalAmount AS Amount,
                       sp.Name AS Space, {CustomerName} AS Customer
                  FROM dbo.WN_Agreements a WITH (NOLOCK)
                  JOIN dbo.WN_Quotations q WITH (NOLOCK) ON q.Id = a.QuotationId
                  JOIN @Spaces sp ON sp.Id = q.SpaceId
                  LEFT JOIN dbo.WN_Customers cu WITH (NOLOCK) ON cu.Id = q.CustomerId
                 WHERE a.BookingId IS NULL AND a.SignedDate IS NULL AND a.SentDate IS NOT NULL
                   AND ISNULL(q.Status, '') NOT IN ('Declined', 'Rejected', 'Cancelled', 'Expired', 'Converted')
                   AND NOT EXISTS (SELECT 1 FROM dbo.WN_Agreements a2 WITH (NOLOCK) WHERE a2.QuotationId = a.QuotationId AND a2.Id > a.Id)
                 ORDER BY a.SentDate;

                -- [2] bookings waiting for confirmation
                SELECT TOP 8 b.Id AS BookingId, CONCAT('#', b.Id) AS Reference, b.StartOn AS [Date], b.TotalAmount AS Amount,
                       sp.Name AS Space, {CustomerName} AS Customer
                  FROM dbo.WN_Bookings b WITH (NOLOCK)
                  JOIN @Spaces sp ON sp.Id = b.SpaceId
                  OUTER APPLY (SELECT TOP 1 * FROM dbo.WN_Customers c2 WITH (NOLOCK) WHERE c2.Code = b.CustomerCode OR c2.UserId = b.UserId) cu
                 WHERE ISNULL(b.IsDeleted, 0) = 0 AND b.BookingStatusId IN (1, 5) AND b.EndOn >= @Now
                 ORDER BY b.StartOn;

                -- [3] tour inquiries (not tied to a location)
                DECLARE @NewInquiries INT = (SELECT COUNT(*) FROM dbo.WN_Contacts WITH (NOLOCK) WHERE StatusId = 1);
                DECLARE @InquiriesInPeriod INT = (SELECT COUNT(*) FROM dbo.WN_Contacts WITH (NOLOCK) WHERE CreatedOn >= @PStart);
                DECLARE @FollowUpsDue INT = 0, @ConvertedInquiries INT = 0;
                IF OBJECT_ID(N'dbo.WN_ContactFeedback', N'U') IS NOT NULL
                BEGIN
                    SELECT @FollowUpsDue = COUNT(*) FROM (
                        SELECT ContactId, Outcome, FollowUpOn, ROW_NUMBER() OVER (PARTITION BY ContactId ORDER BY Id DESC) rn
                          FROM dbo.WN_ContactFeedback WITH (NOLOCK)) f
                     WHERE f.rn = 1 AND f.Outcome = N'future_prospect' AND f.FollowUpOn <= @Today;
                    SELECT @ConvertedInquiries = COUNT(DISTINCT ContactId) FROM dbo.WN_ContactFeedback WITH (NOLOCK)
                     WHERE Outcome = N'converted' AND CreatedOn >= @PStart;
                END
                SELECT @NewInquiries AS NewInquiries, @InquiriesInPeriod AS InquiriesInPeriod,
                       @FollowUpsDue AS FollowUpsDue, @ConvertedInquiries AS ConvertedInquiries;

                -- [4] follow-ups due (future prospects whose date has come)
                IF OBJECT_ID(N'dbo.WN_ContactFeedback', N'U') IS NOT NULL
                    SELECT TOP 8 ct.Id, ct.Name AS Customer, ct.PhoneNumber AS Phone, f.FollowUpOn AS [Date], f.Reason AS Note
                      FROM (SELECT ContactId, Outcome, FollowUpOn, Reason, ROW_NUMBER() OVER (PARTITION BY ContactId ORDER BY Id DESC) rn
                              FROM dbo.WN_ContactFeedback WITH (NOLOCK)) f
                      JOIN dbo.WN_Contacts ct WITH (NOLOCK) ON ct.Id = f.ContactId
                     WHERE f.rn = 1 AND f.Outcome = N'future_prospect' AND f.FollowUpOn <= @Today
                     ORDER BY f.FollowUpOn;
                ELSE
                    SELECT TOP 0 CAST(NULL AS INT) AS Id, CAST(NULL AS NVARCHAR(200)) AS Customer, CAST(NULL AS NVARCHAR(50)) AS Phone,
                           CAST(NULL AS DATE) AS [Date], CAST(NULL AS NVARCHAR(1000)) AS Note;

                -- [5] renewals: running bookings ending in the next @RenewalDays days
                SELECT TOP 10 b.Id AS BookingId, CONCAT('#', b.Id) AS Reference, b.EndOn AS [Date], b.MonthlyRent AS Amount,
                       sp.Name AS Space, {CustomerName} AS Customer
                  FROM dbo.WN_Bookings b WITH (NOLOCK)
                  JOIN @Spaces sp ON sp.Id = b.SpaceId
                  OUTER APPLY (SELECT TOP 1 * FROM dbo.WN_Customers c2 WITH (NOLOCK) WHERE c2.Code = b.CustomerCode OR c2.UserId = b.UserId) cu
                 WHERE ISNULL(b.IsDeleted, 0) = 0 AND b.BookingStatusId IN (2, 33)
                   AND b.EndOn >= @Now AND b.EndOn <= DATEADD(DAY, @RenewalDays, @Now)
                 ORDER BY b.EndOn;
                SELECT COUNT(*) AS RenewalsDue
                  FROM dbo.WN_Bookings b WITH (NOLOCK) JOIN @Spaces sp ON sp.Id = b.SpaceId
                 WHERE ISNULL(b.IsDeleted, 0) = 0 AND b.BookingStatusId IN (2, 33)
                   AND b.EndOn >= @Now AND b.EndOn <= DATEADD(DAY, @RenewalDays, @Now);

                -- [7] collections: overdue invoices (WHT is withheld by the customer, not owed)
                DECLARE @Inv TABLE (Id INT PRIMARY KEY, InvoiceNumber NVARCHAR(50), BookingId INT, UserId INT, DueOn DATE, Balance DECIMAL(18,2), SpaceName NVARCHAR(200), CustomerCode NVARCHAR(50));
                INSERT INTO @Inv
                SELECT i.Id, i.InvoiceNumber, i.BookingId, ISNULL(b.UserId, i.UserId), i.DueOn,
                       ISNULL(i.GrandTotal, 0) - ISNULL(i.WHTAmount, 0) - ISNULL(i.PaidTotal, 0), sp.Name, b.CustomerCode
                  FROM dbo.WN_Invoices i WITH (NOLOCK)
                  JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
                  JOIN @Spaces sp ON sp.Id = b.SpaceId
                 WHERE i.StatusId IN ({IdList(openInvoiceStatusIds)});
                SELECT ISNULL(SUM(Balance), 0) AS Outstanding, COUNT(*) AS OutstandingCount,
                       ISNULL(SUM(CASE WHEN DueOn < @Today THEN Balance ELSE 0 END), 0) AS Overdue,
                       SUM(CASE WHEN DueOn < @Today THEN 1 ELSE 0 END) AS OverdueCount
                  FROM @Inv;
                SELECT TOP 10 inv.BookingId, inv.InvoiceNumber AS Reference, inv.DueOn AS [Date], inv.Balance AS Amount,
                       inv.SpaceName AS Space, {CustomerName} AS Customer
                  FROM @Inv inv
                  OUTER APPLY (SELECT TOP 1 * FROM dbo.WN_Customers c2 WITH (NOLOCK) WHERE c2.Code = inv.CustomerCode OR c2.UserId = inv.UserId) cu
                 WHERE inv.DueOn < @Today
                 ORDER BY inv.DueOn, inv.Id;",
                p =>
                {
                    p.Add("@PStart", SqlDbType.DateTime2).Value = periodStart;
                    p.Add("@Today", SqlDbType.Date).Value = now.Date;
                    p.Add("@BizNow", SqlDbType.DateTime2).Value = now;
                    p.Add("@RenewalDays", SqlDbType.Int).Value = renewalDays;
                });

        public async Task<List<List<IDictionary<string, object?>>>> GetTeamAsync(IReadOnlyCollection<int>? locationIds, DateTime now)
        {
            bool hasLoc;
            await using (var c = new SqlConnection(_connectionString)) { await c.OpenAsync(); hasLoc = await DeviceLocationSql.HasColumnAsync(c); }
            var devLoc = DeviceLocationSql.Filter(hasLoc, "hd", locationIds);
            return await QueryAsync($@"
                {SpacesSql(locationIds)}
                DECLARE @OpenComplaints INT = 0, @UnreadWhatsApp INT = 0, @KycPending INT = 0, @AccessSuspended INT = 0;
                IF OBJECT_ID(N'dbo.WN_Complaints', N'U') IS NOT NULL
                    SELECT @OpenComplaints = COUNT(*) FROM dbo.WN_Complaints WITH (NOLOCK) WHERE Status IN (N'open', N'in_progress');
                IF OBJECT_ID(N'dbo.WN_WhatsApp_Conversations', N'U') IS NOT NULL
                    SELECT @UnreadWhatsApp = COUNT(*) FROM dbo.WN_WhatsApp_Conversations WITH (NOLOCK) WHERE UnreadCount > 0;
                IF OBJECT_ID(N'dbo.WN_CustomerKYCDocuments', N'U') IS NOT NULL
                    SELECT @KycPending = COUNT(DISTINCT d.CustomerId) FROM dbo.WN_CustomerKYCDocuments d WITH (NOLOCK)
                     WHERE d.IsActive = 1 AND d.Status = 0;
                IF OBJECT_ID(N'dbo.WN_HIK_BookingAccessSuspensions', N'U') IS NOT NULL
                    SELECT @AccessSuspended = COUNT(*) FROM dbo.WN_HIK_BookingAccessSuspensions sus WITH (NOLOCK)
                      JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = sus.BookingId
                      JOIN @Spaces sp ON sp.Id = b.SpaceId
                     WHERE sus.ResolvedAt IS NULL AND (sus.OverrideUntil IS NULL OR sus.OverrideUntil < @Today);
                SELECT @OpenComplaints AS OpenComplaints, @UnreadWhatsApp AS UnreadWhatsApp,
                       (SELECT COUNT(*) FROM dbo.WN_Contacts WITH (NOLOCK) WHERE StatusId = 1) AS NewInquiries,
                       @KycPending AS KycPending, @AccessSuspended AS AccessSuspended,
                       (SELECT COUNT(*) FROM dbo.WN_HIK_Devices hd WITH (NOLOCK) WHERE hd.Online = 0{devLoc}) AS MachinesOffline,
                       (SELECT COUNT(*) FROM dbo.WN_HIK_Devices hd WITH (NOLOCK) WHERE 1 = 1{devLoc}) AS Machines;
                SELECT TOP 8 hd.Device_Name AS Name, hd.Last_seen AS LastSeen FROM dbo.WN_HIK_Devices hd WITH (NOLOCK) WHERE hd.Online = 0{devLoc} ORDER BY hd.Device_Name;",
                p => p.Add("@Today", SqlDbType.Date).Value = now.Date);
        }

        public Task<List<List<IDictionary<string, object?>>>> GetLocationComparisonAsync(DateTime periodStart, DateTime now, IEnumerable<int> openInvoiceStatusIds, IEnumerable<int> voidStatusIds) =>
            QueryAsync($@"
                DECLARE @Now DATETIME2(0) = @BizNow;   -- business (Pakistan) time from the app clock
                SELECT l.Id, l.Name,
                       (SELECT COUNT(*) FROM dbo.WN_Spaces s WITH (NOLOCK) WHERE s.Status = 1 AND s.LocationId = l.Id) AS TotalSpaces,
                       (SELECT COUNT(DISTINCT b.SpaceId) FROM dbo.WN_Bookings b WITH (NOLOCK) JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                         WHERE s.LocationId = l.Id AND s.Status = 1 AND ISNULL(b.IsDeleted, 0) = 0 AND b.BookingStatusId IN (1, 2, 5, 33)
                           AND b.StartOn <= @Now AND b.EndOn >= @Now) AS OccupiedSpaces,
                       (SELECT COUNT(*) FROM dbo.WN_Bookings b WITH (NOLOCK) JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                         WHERE s.LocationId = l.Id AND ISNULL(b.IsDeleted, 0) = 0 AND b.BookingStatusId IN (1, 2, 5, 33)
                           AND b.StartOn <= @Now AND b.EndOn >= @Now) AS ActiveBookings,
                       inv.Invoiced, inv.Collected, inv.Outstanding, inv.Overdue
                  FROM dbo.WN_Locations l WITH (NOLOCK)
                 OUTER APPLY (
                       SELECT ISNULL(SUM(CASE WHEN i.IssuedOn >= @PStart THEN i.GrandTotal ELSE 0 END), 0) AS Invoiced,
                              ISNULL(SUM(CASE WHEN i.IssuedOn >= @PStart THEN i.PaidTotal ELSE 0 END), 0) AS Collected,
                              ISNULL(SUM(CASE WHEN i.StatusId IN ({IdList(openInvoiceStatusIds)}) THEN ISNULL(i.GrandTotal, 0) - ISNULL(i.WHTAmount, 0) - ISNULL(i.PaidTotal, 0) ELSE 0 END), 0) AS Outstanding,
                              ISNULL(SUM(CASE WHEN i.StatusId IN ({IdList(openInvoiceStatusIds)}) AND i.DueOn < @Today THEN ISNULL(i.GrandTotal, 0) - ISNULL(i.WHTAmount, 0) - ISNULL(i.PaidTotal, 0) ELSE 0 END), 0) AS Overdue
                         FROM dbo.WN_Invoices i WITH (NOLOCK)
                         JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
                         JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                        WHERE s.LocationId = l.Id AND i.StatusId NOT IN ({IdList(voidStatusIds)})) inv
                 ORDER BY l.Name;",
                p =>
                {
                    p.Add("@PStart", SqlDbType.DateTime2).Value = periodStart;
                    p.Add("@Today", SqlDbType.Date).Value = now.Date;
                    p.Add("@BizNow", SqlDbType.DateTime2).Value = now;
                });

        public Task<List<List<IDictionary<string, object?>>>> GetStaffAsync(DateTime periodStart) =>
            QueryAsync($@"
                -- user -> locations: the multi-location mapping when present, plus the user's main location
                DECLARE @UL TABLE (UserId INT, LocationId INT, PRIMARY KEY (UserId, LocationId));
                INSERT INTO @UL SELECT Id, LocationId FROM dbo.WN_Users WITH (NOLOCK) WHERE IsActive = 1 AND RoleId IN (2, 16) AND LocationId IS NOT NULL;
                IF OBJECT_ID(N'dbo.WN_UserLocations', N'U') IS NOT NULL
                    INSERT INTO @UL SELECT DISTINCT ul.UserId, ul.LocationId FROM dbo.WN_UserLocations ul WITH (NOLOCK)
                      JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = ul.UserId
                     WHERE u.IsActive = 1 AND u.RoleId IN (2, 16)
                       AND NOT EXISTS (SELECT 1 FROM @UL x WHERE x.UserId = ul.UserId AND x.LocationId = ul.LocationId);

                -- [0] staff per location
                SELECT l.Id, l.Name,
                       (SELECT COUNT(*) FROM @UL ul JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = ul.UserId WHERE ul.LocationId = l.Id AND u.RoleId = 2) AS Admins,
                       (SELECT COUNT(*) FROM @UL ul JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = ul.UserId WHERE ul.LocationId = l.Id AND u.RoleId = 16) AS SalesExecutives
                  FROM dbo.WN_Locations l WITH (NOLOCK)
                 ORDER BY l.Name;

                -- [1] sales executives and admins: quotations they created in the period and how many converted
                SELECT TOP 25 u.Id, COALESCE(NULLIF(u.Name, ''), NULLIF(u.UserName, ''), u.Email) AS Name, u.Email, u.RoleId,
                       STUFF((SELECT ', ' + l.Name FROM @UL ul JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = ul.LocationId
                               WHERE ul.UserId = u.Id ORDER BY l.Name FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS Location,
                       COUNT(q.Id) AS Quotations,
                       SUM(CASE WHEN q.Status = 'Converted' THEN 1 ELSE 0 END) AS Converted,
                       ISNULL(SUM(CASE WHEN q.Status = 'Converted' THEN q.TotalAmount ELSE 0 END), 0) AS ConvertedValue
                  FROM dbo.WN_Users u WITH (NOLOCK)
                  LEFT JOIN dbo.WN_Quotations q WITH (NOLOCK) ON q.CreatedById = u.Id AND q.CreatedDate >= @PStart AND ISNULL(q.IsActive, 1) = 1
                 WHERE u.IsActive = 1 AND u.RoleId IN (2, 16)
                 GROUP BY u.Id, u.Name, u.UserName, u.Email, u.RoleId
                 ORDER BY COUNT(q.Id) DESC, COALESCE(NULLIF(u.Name, ''), NULLIF(u.UserName, ''), u.Email);",
                p => p.Add("@PStart", SqlDbType.DateTime2).Value = periodStart);

        public Task<List<List<IDictionary<string, object?>>>> GetSystemAsync() =>
            QueryAsync(@"
                DECLARE @EmailPending INT = 0, @EmailFailed INT = 0, @LastWhatsApp DATETIME2(0) = NULL;
                IF OBJECT_ID(N'dbo.WN_InvoiceDeliveryQueue', N'U') IS NOT NULL
                    SELECT @EmailPending = SUM(CASE WHEN Status = 'Pending' THEN 1 ELSE 0 END),
                           @EmailFailed  = SUM(CASE WHEN Status LIKE 'Failed%' THEN 1 ELSE 0 END)
                      FROM dbo.WN_InvoiceDeliveryQueue WITH (NOLOCK);
                IF OBJECT_ID(N'dbo.WN_WhatsApp_Conversations', N'U') IS NOT NULL
                    SELECT @LastWhatsApp = MAX(LastIncomingAt) FROM dbo.WN_WhatsApp_Conversations WITH (NOLOCK);
                SELECT ISNULL(@EmailPending, 0) AS EmailPending, ISNULL(@EmailFailed, 0) AS EmailFailed, @LastWhatsApp AS LastWhatsAppMessage,
                       (SELECT COUNT(*) FROM dbo.WN_HIK_Devices WITH (NOLOCK) WHERE Online = 0) AS MachinesOffline,
                       (SELECT COUNT(*) FROM dbo.WN_HIK_Devices WITH (NOLOCK)) AS Machines,
                       (SELECT MAX(Last_seen) FROM dbo.WN_HIK_Devices WITH (NOLOCK)) AS LastMachineSeen;",
                _ => { });
    }
}
