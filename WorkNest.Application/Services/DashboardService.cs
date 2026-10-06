using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class DashboardService : IDashboardService
    {
        private const int EndingSoonDays = 30;

        private readonly IDbRepository _db;
        private readonly IOrderStatusService _orderStatus;
        private readonly IBusinessClock _clock;
        public DashboardService(IDbRepository db, IOrderStatusService orderStatus, IBusinessClock clock)
        {
            _clock = clock;
            _db = db;
            _orderStatus = orderStatus;
        }

        public async Task<ApiResponse> GetSummaryAsync()
        {
            var results = await _db.GetDashboardSummaryAsync();
            return ApiResponse.Ok(results);
        }

        // Sidebar route -> badge key in GetNavBadgeItemsDbAsync.
        private static readonly (string Route, string Key)[] NavBadgeRoutes =
        {
            ("/admin/agreements", "Agreements"), ("/admin/contacts", "Contacts"), ("/admin/bookings", "Bookings"),
            ("/admin/quotations", "Quotations"), ("/admin/kyc", "Kyc"), ("/admin/invoices", "Invoices"),
            ("/admin/attendants", "AccessSuspended"), ("/admin/access-dashboard", "MachinesOffline")
        };

        /// <summary>Items waiting on staff right now, grouped by sidebar route.</summary>
        private async Task<Dictionary<string, List<string>>> GetNavBadgeItemsAsync(int? locationId, bool includeMachines)
        {
            var st = await _orderStatus.GetInvoiceStatusesAsync();
            // Still owed = Unpaid / Partial / Challan Expire (legacy 1, 3, 4 + OrderStatus IDs looked up by description).
            var open = st.Unpaid.Concat(st.Partial).Concat(st.Overdue);
            var rows = await _db.GetNavBadgeItemsDbAsync(locationId, open, _clock.Today, _clock.Now);

            var result = new Dictionary<string, List<string>>();
            foreach (var (route, key) in NavBadgeRoutes)
            {
                if (key == "MachinesOffline" && !includeMachines) continue;
                // KYC is left out entirely when its table is missing (no rows can't be told apart, so keep 0).
                result[route] = rows.Where(r => r.Key == key).Select(r => r.ItemKey).Distinct().ToList();
            }
            return result;
        }

        public async Task<Dictionary<string, int>> GetNavBadgesAsync(int? locationId, bool includeMachines, string? userEmail)
        {
            var items = await GetNavBadgeItemsAsync(locationId, includeMachines);
            var reads = string.IsNullOrWhiteSpace(userEmail)
                ? new Dictionary<string, HashSet<string>>()
                : await _db.GetNavBadgeReadsDbAsync(userEmail);
            return items.ToDictionary(
                kv => kv.Key,
                kv => reads.TryGetValue(kv.Key, out var read) ? kv.Value.Count(i => !read.Contains(i)) : kv.Value.Count);
        }

        public async Task<bool> MarkNavBadgesReadAsync(int? locationId, string userEmail, IEnumerable<string> routes)
        {
            var wanted = new HashSet<string>(routes, StringComparer.OrdinalIgnoreCase);
            var items = await GetNavBadgeItemsAsync(locationId, includeMachines: true);
            // Only what is waiting right now is stored, so the list never grows beyond the open items.
            var toSave = items.Where(kv => wanted.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            return toSave.Count == 0 || await _db.SaveNavBadgeReadsDbAsync(userEmail, toSave);
        }

        public async Task<DashboardOverviewDto> GetOverviewAsync(int? locationId, string? period = null)
        {
            period = (period ?? "month").Trim().ToLowerInvariant();
            if (period is not ("month" or "quarter" or "year")) period = "month";
            var st = await _orderStatus.GetInvoiceStatusesAsync();
            // Open = still owed (Unpaid / Partial / Overdue, legacy + OrderStatus); void = legacy 5 + OrderStatus Cancelled.
            var open = st.Unpaid.Concat(st.Partial).Concat(st.Overdue);
            var voids = st.Cancelled.Append(5);
            var sets = await _db.GetDashboardOverviewDbAsync(locationId, EndingSoonDays, open, st.Paid, voids, period, _clock.Now);

            var k = sets.Count > 0 ? sets[0].FirstOrDefault() : null;
            int Int(IDictionary<string, object?>? r, string key) => r != null && r.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v) : 0;
            decimal Dec(IDictionary<string, object?>? r, string key) => r != null && r.TryGetValue(key, out var v) && v != null ? Convert.ToDecimal(v) : 0m;
            decimal Pct(int part, int total) => total > 0 ? Math.Round(part * 100m / total, 1) : 0m;

            var dto = new DashboardOverviewDto
            {
                TotalSpaces = Int(k, "TotalSpaces"),
                OccupiedSpaces = Int(k, "OccupiedSpaces"),
                OccupiedSpacesLastMonth = Int(k, "OccupiedSpacesLastMonth"),
                ActiveBookings = Int(k, "ActiveBookings"),
                ActiveBookingsLastMonth = Int(k, "ActiveBookingsLastMonth"),
                PendingConfirmations = Int(k, "PendingConfirmations"),
                Outstanding = Math.Round(Dec(k, "Outstanding"), 2),
                OutstandingCount = Int(k, "OutstandingCount"),
                OverdueAmount = Math.Round(Dec(k, "OverdueAmount"), 2),
                OverdueCount = Int(k, "OverdueCount"),
                Period = period,
                InvoicedThisPeriod = Math.Round(Dec(k, "InvoicedThisMonth"), 2),
                InvoicedPrevPeriod = Math.Round(Dec(k, "InvoicedLastMonth"), 2),
                PaidThisPeriod = Math.Round(Dec(k, "PaidThisPeriod"), 2),
                LeasesEndingSoon = Int(k, "LeasesEndingSoon"),
                EndingSoonDays = EndingSoonDays,
                GeneratedAt = _clock.Now.ToString("yyyy-MM-ddTHH:mm:ss")
            };
            dto.OccupancyPct = Pct(dto.OccupiedSpaces, dto.TotalSpaces);
            dto.OccupancyPctLastMonth = Pct(dto.OccupiedSpacesLastMonth, dto.TotalSpaces);

            foreach (var m in sets.Count > 1 ? sets[1] : new())
                dto.Months.Add(new DashboardMonthDto
                {
                    Month = Convert.ToString(m["Month"]) ?? "",
                    Invoiced = Math.Round(Dec(m, "Invoiced"), 2),
                    Paid = Math.Round(Dec(m, "Paid"), 2),
                    OccupancyPct = Pct(Int(m, "Occupied"), dto.TotalSpaces),
                    NewBookings = Int(m, "NewBookings")
                });

            static List<DashboardAttentionItemDto> Items(List<List<IDictionary<string, object?>>> s, int i) =>
                (i < s.Count ? s[i] : new()).Select(r => new DashboardAttentionItemDto
                {
                    BookingId = r["BookingId"] != null ? Convert.ToInt32(r["BookingId"]) : null,
                    Reference = Convert.ToString(r["Reference"]),
                    Customer = Convert.ToString(r["Customer"]),
                    Space = Convert.ToString(r["Space"]),
                    Date = r["Date"] is DateTime d ? d.ToString("yyyy-MM-dd") : null,
                    Amount = r["Amount"] != null ? Math.Round(Convert.ToDecimal(r["Amount"]), 2) : null
                }).ToList();

            dto.OverdueInvoices = Items(sets, 2);
            dto.EndingLeases = Items(sets, 3);
            dto.PendingBookings = Items(sets, 4);

            List<IDictionary<string, object?>> Set(int i) => i < sets.Count ? sets[i] : new();
            dto.Aging = Set(5).Select(r => new DashboardBucketDto
                { Label = Convert.ToString(r["Bucket"]) ?? "", Amount = Math.Round(Dec(r, "Amount"), 2), Count = Int(r, "Invoices") }).ToList();
            dto.SpaceTypes = Set(6).Select(r => new DashboardSpaceTypeDto
                { SpaceType = Convert.ToString(r["SpaceType"]) ?? "", Total = Int(r, "Total"), Occupied = Int(r, "Occupied") }).ToList();
            dto.TopCustomers = Set(7).Select(r => new DashboardBucketDto
                { Label = Convert.ToString(r["Customer"]) ?? "", Amount = Math.Round(Dec(r, "Amount"), 2), Count = Int(r, "Invoices") }).ToList();
            dto.LeaseExpiries = Set(8).Select(r => new DashboardBucketDto
                { Label = Convert.ToString(r["Month"]) ?? "", Amount = Math.Round(Dec(r, "Value"), 2), Count = Int(r, "Leases") }).ToList();
            return dto;
        }
    }
}
