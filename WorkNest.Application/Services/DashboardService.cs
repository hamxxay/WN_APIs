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
