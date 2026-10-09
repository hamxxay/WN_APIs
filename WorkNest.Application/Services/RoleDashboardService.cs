using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Dashboard sections by role. Sales executive: sales pipeline, tour inquiries &amp; follow-ups, renewals and
    /// collections for their locations. Admin: team &amp; service for their locations (branch operations and finance
    /// come from the overview). Super admin: team &amp; service, location comparison, staff and system health.
    /// </summary>
    public class RoleDashboardService : IRoleDashboardService
    {
        private const int RenewalDays = 60;

        private readonly IRoleDashboardRepository _repo;
        private readonly IOrderStatusService _orderStatus;
        private readonly IBusinessClock _clock;
        private readonly IHikSyncService _hikSync;
        private readonly HikSyncOptions _hikOptions;
        private readonly IWhatsAppClient _whatsApp;

        public RoleDashboardService(IRoleDashboardRepository repo, IOrderStatusService orderStatus, IBusinessClock clock,
            IHikSyncService hikSync, HikSyncOptions hikOptions, IWhatsAppClient whatsApp)
        {
            _repo = repo;
            _orderStatus = orderStatus;
            _clock = clock;
            _hikSync = hikSync;
            _hikOptions = hikOptions;
            _whatsApp = whatsApp;
        }

        public async Task<RoleDashboardDto> GetAsync(string role, IReadOnlyCollection<int>? locationIds, string? period)
        {
            period = (period ?? "month").Trim().ToLowerInvariant();
            if (period is not ("month" or "quarter" or "year")) period = "month";
            var now = _clock.Now;
            var today = now.Date;
            // Same calendar periods as the overview: this month / quarter / year to date.
            var periodStart = period switch
            {
                "year" => new DateTime(today.Year, 1, 1),
                "quarter" => new DateTime(today.Year, (today.Month - 1) / 3 * 3 + 1, 1),
                _ => new DateTime(today.Year, today.Month, 1)
            };

            var st = await _orderStatus.GetInvoiceStatusesAsync();
            var open = st.Unpaid.Concat(st.Partial).Concat(st.Overdue).ToList();
            var voids = st.Cancelled.Append(5).ToList();

            var dto = new RoleDashboardDto { Role = role, Period = period, GeneratedAt = now.ToString("yyyy-MM-ddTHH:mm:ss") };
            if (role == "sales_executive")
            {
                dto.Sales = await GetSalesAsync(locationIds, periodStart, now, open);
                return dto;
            }

            dto.Team = await GetTeamAsync(locationIds, now);
            if (role == "super_admin")
            {
                dto.Locations = await GetLocationsAsync(periodStart, now, open, voids);
                dto.Staff = await GetStaffAsync(periodStart);
                dto.System = await GetSystemAsync();
            }
            return dto;
        }

        // --- row helpers ---
        private static List<IDictionary<string, object?>> Set(List<List<IDictionary<string, object?>>> s, int i) => i < s.Count ? s[i] : new();
        private static object? Val(IDictionary<string, object?>? r, string key) => r != null && r.TryGetValue(key, out var v) ? v : null;
        private static int Int(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToInt32(v) : 0;
        private static decimal Dec(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Math.Round(Convert.ToDecimal(v), 2) : 0m;
        private static string? Str(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToString(v) : null;
        private static string? Date(IDictionary<string, object?>? r, string key) => Val(r, key) is DateTime d ? d.ToString("yyyy-MM-dd") : null;
        private static string? DateTimeText(object? v) => v is DateTime d ? d.ToString("yyyy-MM-ddTHH:mm:ss") : null;
        private static decimal Pct(int part, int total) => total > 0 ? Math.Round(part * 100m / total, 1) : 0m;

        private static List<DashboardAttentionItemDto> Items(List<IDictionary<string, object?>> rows) =>
            rows.Select(r => new DashboardAttentionItemDto
            {
                BookingId = Val(r, "BookingId") is { } b ? Convert.ToInt32(b) : null,
                Reference = Str(r, "Reference"),
                Customer = Str(r, "Customer"),
                Space = Str(r, "Space"),
                Date = Date(r, "Date"),
                Amount = Val(r, "Amount") != null ? Dec(r, "Amount") : null
            }).ToList();

        private async Task<SalesDashboardDto> GetSalesAsync(IReadOnlyCollection<int>? locationIds, DateTime periodStart, DateTime now, List<int> open)
        {
            var s = await _repo.GetSalesAsync(locationIds, periodStart, now, open, RenewalDays);
            var p = Set(s, 0).FirstOrDefault();
            var inq = Set(s, 3).FirstOrDefault();
            var col = Set(s, 7).FirstOrDefault();
            var dto = new SalesDashboardDto
            {
                QuotationsCreated = Int(p, "Created"),
                Draft = Int(p, "Draft"),
                Sent = Int(p, "Sent"),
                Accepted = Int(p, "Accepted"),
                AgreementStage = Int(p, "AgreementStage"),
                Converted = Int(p, "Converted"),
                Lost = Int(p, "Lost"),
                ConvertedValue = Dec(p, "ConvertedValue"),
                AwaitingSignature = Items(Set(s, 1)),
                PendingBookings = Items(Set(s, 2)),
                NewInquiries = Int(inq, "NewInquiries"),
                InquiriesInPeriod = Int(inq, "InquiriesInPeriod"),
                FollowUpsDue = Int(inq, "FollowUpsDue"),
                ConvertedInquiries = Int(inq, "ConvertedInquiries"),
                FollowUps = Set(s, 4).Select(r => new FollowUpItemDto
                {
                    ContactId = Int(r, "Id"), Name = Str(r, "Customer"), Phone = Str(r, "Phone"),
                    FollowUpOn = Date(r, "Date"), Note = Str(r, "Note")
                }).ToList(),
                RenewalDays = RenewalDays,
                Renewals = Items(Set(s, 5)),
                RenewalsDue = Int(Set(s, 6).FirstOrDefault(), "RenewalsDue"),
                Outstanding = Dec(col, "Outstanding"),
                OutstandingCount = Int(col, "OutstandingCount"),
                Overdue = Dec(col, "Overdue"),
                OverdueCount = Int(col, "OverdueCount"),
                OverdueInvoices = Items(Set(s, 8))
            };
            dto.ConversionPct = Pct(dto.Converted, dto.QuotationsCreated);
            return dto;
        }

        private async Task<TeamDashboardDto> GetTeamAsync(IReadOnlyCollection<int>? locationIds, DateTime now)
        {
            var s = await _repo.GetTeamAsync(locationIds, now);
            var k = Set(s, 0).FirstOrDefault();
            return new TeamDashboardDto
            {
                OpenComplaints = Int(k, "OpenComplaints"),
                UnreadWhatsApp = Int(k, "UnreadWhatsApp"),
                NewInquiries = Int(k, "NewInquiries"),
                KycPending = Int(k, "KycPending"),
                AccessSuspended = Int(k, "AccessSuspended"),
                MachinesOffline = Int(k, "MachinesOffline"),
                Machines = Int(k, "Machines"),
                OfflineMachines = Set(s, 1).Select(r => new OfflineMachineDto
                    { Name = Str(r, "Name") ?? "", LastSeen = DateTimeText(Val(r, "LastSeen")) }).ToList()
            };
        }

        private async Task<List<LocationComparisonDto>> GetLocationsAsync(DateTime periodStart, DateTime now, List<int> open, List<int> voids)
        {
            var s = await _repo.GetLocationComparisonAsync(periodStart, now, open, voids);
            return Set(s, 0).Select(r =>
            {
                var total = Int(r, "TotalSpaces");
                var occupied = Int(r, "OccupiedSpaces");
                return new LocationComparisonDto
                {
                    LocationId = Int(r, "Id"), Name = Str(r, "Name") ?? "",
                    TotalSpaces = total, OccupiedSpaces = occupied, OccupancyPct = Pct(occupied, total),
                    ActiveBookings = Int(r, "ActiveBookings"),
                    Invoiced = Dec(r, "Invoiced"), Collected = Dec(r, "Collected"),
                    Outstanding = Dec(r, "Outstanding"), Overdue = Dec(r, "Overdue")
                };
            }).ToList();
        }

        private async Task<StaffDashboardDto> GetStaffAsync(DateTime periodStart)
        {
            var s = await _repo.GetStaffAsync(periodStart);
            return new StaffDashboardDto
            {
                PerLocation = Set(s, 0).Select(r => new StaffPerLocationDto
                    { LocationId = Int(r, "Id"), Name = Str(r, "Name") ?? "", Admins = Int(r, "Admins"), SalesExecutives = Int(r, "SalesExecutives") }).ToList(),
                People = Set(s, 1).Select(r => new StaffPerformanceDto
                {
                    UserId = Int(r, "Id"), Name = Str(r, "Name") ?? "",
                    Role = Int(r, "RoleId") == WorkNest.Common.Constants.Roles.AdminId ? "Admin" : "Sales Executive",
                    Locations = Str(r, "Location"),
                    Quotations = Int(r, "Quotations"), Converted = Int(r, "Converted"), ConvertedValue = Dec(r, "ConvertedValue")
                }).ToList()
            };
        }

        private async Task<SystemHealthDto> GetSystemAsync()
        {
            var s = await _repo.GetSystemAsync();
            var k = Set(s, 0).FirstOrDefault();
            return new SystemHealthDto
            {
                HikSyncEnabled = _hikOptions.Enabled,
                HikJobs = _hikSync.GetStatus().Select(j => new SystemJobDto
                {
                    Job = j.Job, LastFinishedAt = DateTimeText(j.LastFinishedAt), LastOk = j.LastOk,
                    LastResult = j.LastResult, Running = j.Running
                }).ToList(),
                Machines = Int(k, "Machines"),
                MachinesOffline = Int(k, "MachinesOffline"),
                LastMachineSeen = DateTimeText(Val(k, "LastMachineSeen")),
                WhatsAppConfigured = _whatsApp.IsConfigured,
                LastWhatsAppMessage = DateTimeText(Val(k, "LastWhatsAppMessage")),
                EmailPending = Int(k, "EmailPending"),
                EmailFailed = Int(k, "EmailFailed")
            };
        }
    }
}
