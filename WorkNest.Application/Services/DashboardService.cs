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
        private readonly IComplaintRepository _complaints;
        private readonly IWhatsAppRepository _whatsApp;
        public DashboardService(IDbRepository db, IOrderStatusService orderStatus, IBusinessClock clock, IComplaintRepository complaints, IWhatsAppRepository whatsApp)
        {
            _complaints = complaints;
            _whatsApp = whatsApp;
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
        /// <param name="branchLocationIds">Location-bound staff's locations: tour inquiries and complaints of those branches plus
        /// the ones without a branch (BR_01 branch routing); null = all.</param>
        private async Task<Dictionary<string, List<string>>> GetNavBadgeItemsAsync(IReadOnlyCollection<int>? locationIds, bool includeMachines,
            IReadOnlyCollection<int>? branchLocationIds)
        {
            var st = await _orderStatus.GetInvoiceStatusesAsync();
            // Still owed = Unpaid / Partial / Challan Expire (legacy 1, 3, 4 + OrderStatus IDs looked up by description).
            var open = st.Unpaid.Concat(st.Partial).Concat(st.Overdue);
            var rows = await _db.GetNavBadgeItemsDbAsync(locationIds, open, _clock.Today, _clock.Now, branchLocationIds);

            var result = new Dictionary<string, List<string>>();
            foreach (var (route, key) in NavBadgeRoutes)
            {
                if (key == "MachinesOffline" && !includeMachines) continue;
                // KYC is left out entirely when its table is missing (no rows can't be told apart, so keep 0).
                result[route] = rows.Where(r => r.Key == key).Select(r => r.ItemKey).Distinct().ToList();
            }
            // Tour inquiries marked "future prospect" whose follow-up date has come: alert on Tour Inquiries
            // (one item per inquiry + date, so a new follow-up date alerts again even after "mark as read").
            if (result.TryGetValue("/admin/contacts", out var contactItems))
                foreach (var (contactId, followUpOn) in await _db.GetDueContactFollowUpsAsync(_clock.Today, branchLocationIds))
                    contactItems.Add($"followup:{contactId}:{followUpOn:yyyyMMdd}");
            // New (open) complaints waiting on staff.
            result["/admin/complaints"] = (await _complaints.GetOpenIdsAsync(branchLocationIds)).Select(id => $"complaint:{id}").ToList();
            // WhatsApp conversations with unread customer messages (a new message alerts again).
            result["/admin/whatsapp"] = (await _whatsApp.GetUnreadConversationsAsync()).Select(x => $"wa:{x.ConversationId}:{x.LastMessageAt:yyyyMMddHHmmss}").ToList();
            return result;
        }

        public async Task<Dictionary<string, int>> GetNavBadgesAsync(IReadOnlyCollection<int>? locationIds, bool includeMachines, string? userEmail,
            IReadOnlyCollection<int>? branchLocationIds = null)
        {
            var items = await GetNavBadgeItemsAsync(locationIds, includeMachines, branchLocationIds);
            var reads = string.IsNullOrWhiteSpace(userEmail)
                ? new Dictionary<string, HashSet<string>>()
                : await _db.GetNavBadgeReadsDbAsync(userEmail);
            return items.ToDictionary(
                kv => kv.Key,
                kv => reads.TryGetValue(kv.Key, out var read) ? kv.Value.Count(i => !read.Contains(i)) : kv.Value.Count);
        }

        public async Task<bool> MarkNavBadgesReadAsync(IReadOnlyCollection<int>? locationIds, string userEmail, IEnumerable<string> routes,
            IReadOnlyCollection<int>? branchLocationIds = null)
        {
            var wanted = new HashSet<string>(routes, StringComparer.OrdinalIgnoreCase);
            var items = await GetNavBadgeItemsAsync(locationIds, includeMachines: true, branchLocationIds);
            // Only what is waiting right now is stored, so the list never grows beyond the open items.
            var toSave = items.Where(kv => wanted.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            return toSave.Count == 0 || await _db.SaveNavBadgeReadsDbAsync(userEmail, toSave);
        }

        public async Task<DashboardOverviewDto> GetOverviewAsync(IReadOnlyCollection<int>? locationIds, string? period = null)
        {
            period = (period ?? "month").Trim().ToLowerInvariant();
            if (period is not ("month" or "quarter" or "year")) period = "month";
            var st = await _orderStatus.GetInvoiceStatusesAsync();
            // Open = still owed (Unpaid / Partial / Overdue, legacy + OrderStatus); void = legacy 5 + OrderStatus Cancelled.
            var open = st.Unpaid.Concat(st.Partial).Concat(st.Overdue);
            var voids = st.Cancelled.Append(5);
            var sets = await _db.GetDashboardOverviewDbAsync(locationIds, EndingSoonDays, open, st.Paid, voids, period, _clock.Now);

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

        public async Task<DashboardOccupiedSpacesDto> GetOccupiedSpacesAsync(IReadOnlyCollection<int>? locationIds)
        {
            var now = _clock.Now;
            var sets = await _db.GetDashboardOccupiedSpacesDbAsync(locationIds, now);

            static object? Val(IDictionary<string, object?> r, string key) => r.TryGetValue(key, out var v) ? v : null;
            static int? IntOrNull(IDictionary<string, object?> r, string key) => Val(r, key) is { } v ? Convert.ToInt32(v) : null;
            static string? Str(IDictionary<string, object?> r, string key) => Val(r, key) is { } v ? Convert.ToString(v) : null;
            static string? Time(IDictionary<string, object?> r, string key) => Val(r, key) is DateTime d ? d.ToString("yyyy-MM-ddTHH:mm:ss") : null;

            var head = sets.Count > 0 ? sets[0].FirstOrDefault() : null;
            var rows = sets.Count > 1 ? sets[1] : new List<IDictionary<string, object?>>();
            var dto = new DashboardOccupiedSpacesDto
            {
                TotalSpaces = head != null ? IntOrNull(head, "TotalSpaces") ?? 0 : 0,
                GeneratedAt = now.ToString("yyyy-MM-ddTHH:mm:ss"),
                Items = rows.Select(r => new DashboardOccupiedSpaceDto
                {
                    SpaceId = IntOrNull(r, "SpaceId") ?? 0,
                    Room = Str(r, "Room") ?? "",
                    SpaceCode = Str(r, "SpaceCode"),
                    LocationId = IntOrNull(r, "LocationId"),
                    Location = Str(r, "Location"),
                    SpaceType = Str(r, "SpaceType") ?? "",
                    Seats = IntOrNull(r, "Seats"),
                    BookedSeats = IntOrNull(r, "BookedSeats"),
                    BookingId = IntOrNull(r, "BookingId") ?? 0,
                    Customer = Str(r, "Customer") ?? "",
                    CustomerCode = Str(r, "CustomerCode"),
                    StartOn = Time(r, "StartOn"),
                    EndOn = Time(r, "EndOn"),
                    StatusId = IntOrNull(r, "StatusId") ?? 0,
                    Status = Str(r, "Status") ?? "",
                    BookingCount = Math.Max(1, IntOrNull(r, "BookingCount") ?? 1)
                }).ToList()
            };
            dto.OccupiedSpaces = dto.Items.Count;
            dto.OccupancyPct = dto.TotalSpaces > 0 ? Math.Round(dto.OccupiedSpaces * 100m / dto.TotalSpaces, 1) : 0m;
            return dto;
        }

        // ---------- Overview chart drill-down (DASH_02) ----------

        private const int DetailRows = 200;
        private const int DetailSpaceRows = 500;

        /// <summary>Aging bucket keys, in chart order, with the drawer title.</summary>
        private static readonly Dictionary<string, string> AgingTitles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["not_due"] = "Open invoices not yet due",
            ["1_30"] = "Open invoices 1-30 days past due",
            ["31_60"] = "Open invoices 31-60 days past due",
            ["61_90"] = "Open invoices 61-90 days past due",
            ["90_plus"] = "Open invoices 90+ days past due"
        };

        public async Task<ApiResponse> GetOverviewDetailsAsync(string kind, IReadOnlyCollection<int>? locationIds, DashboardOverviewDetailsQuery query)
        {
            kind = (kind ?? "").Trim().ToLowerInvariant();
            if (!DashboardOverviewDetails.Kinds.Contains(kind))
                return ApiResponse.Fail($"kind must be one of {string.Join(", ", DashboardOverviewDetails.Kinds)}.");

            var now = _clock.Now;
            var today = _clock.Today;
            // Period as the overview reads it (anything else = month); from + to (both, inclusive) replace it.
            var period = (query.Period ?? "month").Trim().ToLowerInvariant();
            if (period is not ("month" or "quarter" or "year")) period = "month";
            DateTime? from = null, to = null;
            if (!string.IsNullOrWhiteSpace(query.From) || !string.IsNullOrWhiteSpace(query.To))
            {
                if (!TryDay(query.From, out var f) || !TryDay(query.To, out var t))
                    return ApiResponse.Fail("from and to go together, as dates (yyyy-MM-dd).");
                if (f > t) return ApiResponse.Fail("from must be on or before to.");
                from = f; to = t;
            }

            DateTime month = default;
            if (kind is "revenue-month" or "new-bookings" or "leases-ending" or "occupied-month" && !TryMonth(query.Month, out month))
                return ApiResponse.Fail("month must be yyyy-MM (e.g. 2026-10).");
            var monthLabel = month == default ? "" : month.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

            var dto = new DashboardOverviewDetailsDto { Kind = kind, GeneratedAt = now.ToString("yyyy-MM-ddTHH:mm:ss") };
            List<List<IDictionary<string, object?>>>? sets;
            switch (kind)
            {
                case "revenue-month":
                case "aging-bucket":
                case "top-customer":
                {
                    string? bucket = null, customer = null;
                    DateTime? monthStart = null, invFrom = null, invTo = null;
                    if (kind == "revenue-month")
                    {
                        monthStart = month;
                        dto.Title = $"Invoices issued in {monthLabel}";
                        dto.From = Day(month); dto.To = Day(month.AddMonths(1).AddDays(-1));
                    }
                    else if (kind == "aging-bucket")
                    {
                        bucket = (query.Bucket ?? "").Trim().ToLowerInvariant();
                        if (!AgingTitles.TryGetValue(bucket, out var title))
                            return ApiResponse.Fail("bucket must be not_due, 1_30, 31_60, 61_90 or 90_plus.");
                        dto.Title = title;
                    }
                    else
                    {
                        customer = query.Customer;
                        if (string.IsNullOrWhiteSpace(customer)) return ApiResponse.Fail("customer is required.");
                        // The overview's Top customers: issued since the start of the period (no end); a custom range: from - to.
                        invFrom = from ?? PeriodStart(period, today);
                        invTo = to;
                        dto.Title = $"Invoices of {customer}";
                        dto.From = Day(invFrom.Value); dto.To = invTo is { } e ? Day(e) : null;
                    }
                    var st = await _orderStatus.GetInvoiceStatusesAsync();
                    // Same sets as the overview: open = Unpaid / Partial / Challan Expire; void = Cancelled + legacy 5.
                    var open = st.Unpaid.Concat(st.Partial).Concat(st.Overdue);
                    var voids = st.Cancelled.Append(5);
                    sets = await _db.GetOverviewDetailInvoicesDbAsync(locationIds, now, open, st.Paid, voids,
                        monthStart, bucket, customer, invFrom, invTo, DetailRows);
                    if (sets is null) return ApiResponse.Fail(DashboardOverviewDetails.DbUpdateMessage);
                    dto.Records = "invoices";
                    var head = Head(sets);
                    dto.Total = Int(head, "Total");
                    dto.Amount = Money(Dec(head, "Amount"));
                    dto.PaidAmount = Money(Dec(head, "PaidAmount"));
                    dto.Balance = Money(Dec(head, "Balance"));
                    dto.Rows = Rows(sets).Select(r => (object)new DashboardDetailInvoiceDto
                    {
                        InvoiceId = Int(r, "InvoiceId"),
                        InvoiceNumber = Str(r, "InvoiceNumber"),
                        BookingId = IntOrNull(r, "BookingId"),
                        Customer = Str(r, "Customer") ?? "Customer",
                        Space = Str(r, "Space"),
                        Location = Str(r, "Location"),
                        IssuedOn = DayOf(r, "IssuedOn"),
                        DueOn = DayOf(r, "DueOn"),
                        Total = Money(Dec(r, "Total")),
                        Paid = Money(Dec(r, "Paid")),
                        Wht = Money(Dec(r, "Wht")),
                        Balance = Money(Dec(r, "Balance")),
                        IsOpen = Bool(r, "IsOpen"),
                        IsPaid = Bool(r, "IsPaid"),
                        DaysOverdue = Int(r, "DaysOverdue"),
                        Status = Str(r, "Status") ?? ""
                    }).ToList();
                    break;
                }
                case "new-bookings":
                case "leases-ending":
                {
                    var list = kind == "new-bookings" ? "new" : "ending";
                    sets = await _db.GetOverviewDetailBookingsDbAsync(locationIds, now, list, month, DetailRows);
                    if (sets is null) return ApiResponse.Fail(DashboardOverviewDetails.DbUpdateMessage);
                    dto.Records = "bookings";
                    dto.Title = kind == "new-bookings" ? $"Bookings made in {monthLabel}" : $"Leases ending in {monthLabel}";
                    dto.From = Day(month); dto.To = Day(month.AddMonths(1).AddDays(-1));
                    var head = Head(sets);
                    dto.Total = Int(head, "Total");
                    dto.Amount = Money(Dec(head, "Amount"));
                    dto.Rows = Rows(sets).Select(r => (object)new DashboardDetailBookingDto
                    {
                        BookingId = Int(r, "BookingId"),
                        Customer = Str(r, "Customer") ?? "Customer",
                        Space = Str(r, "Space"),
                        SpaceType = Str(r, "SpaceType"),
                        LocationId = IntOrNull(r, "LocationId"),
                        Location = Str(r, "Location"),
                        // An audit column (UTC, DbDateTimeKinds): shown in business time. The month is counted on the stored value, as the chart.
                        CreatedOn = Val(r, "CreatedOn") is DateTime c
                            ? (c.Kind == DateTimeKind.Utc ? _clock.FromUtc(c) : c).ToString("yyyy-MM-ddTHH:mm:ss") : null,
                        StartOn = TimeOf(r, "StartOn"),
                        EndOn = TimeOf(r, "EndOn"),
                        StatusId = Int(r, "StatusId"),
                        Status = Str(r, "Status") ?? "",
                        TotalAmount = Money(Dec(r, "TotalAmount")),
                        Seats = IntOrNull(r, "Seats")
                    }).ToList();
                    break;
                }
                default: // space-type, occupied-month
                {
                    string? spaceType = null;
                    if (kind == "space-type")
                    {
                        spaceType = query.SpaceType;
                        if (string.IsNullOrWhiteSpace(spaceType)) return ApiResponse.Fail("spaceType is required.");
                    }
                    var list = kind == "space-type" ? "type" : "occupied";
                    sets = await _db.GetOverviewDetailSpacesDbAsync(locationIds, now, list, spaceType,
                        kind == "occupied-month" ? month : null, DetailSpaceRows);
                    if (sets is null) return ApiResponse.Fail(DashboardOverviewDetails.DbUpdateMessage);
                    dto.Records = "spaces";
                    var thisMonth = new DateTime(today.Year, today.Month, 1);
                    dto.Title = kind == "space-type" ? $"{spaceType} spaces"
                        : month >= thisMonth ? "Spaces in use now" : $"Spaces in use at the end of {monthLabel}";
                    var head = Head(sets);
                    dto.Total = Int(head, "Total");
                    dto.Occupied = Int(head, "Occupied");
                    dto.TotalSpaces = Int(head, "TotalSpaces");
                    dto.Rows = Rows(sets).Select(r => (object)new DashboardDetailSpaceDto
                    {
                        SpaceId = Int(r, "SpaceId"),
                        Name = Str(r, "Room") ?? "",
                        SpaceCode = Str(r, "SpaceCode"),
                        SpaceType = Str(r, "SpaceType") ?? "",
                        LocationId = IntOrNull(r, "LocationId"),
                        Location = Str(r, "Location"),
                        Capacity = IntOrNull(r, "Seats"),
                        Occupied = Bool(r, "Occupied"),
                        BookingId = IntOrNull(r, "BookingId"),
                        Customer = Str(r, "Customer"),
                        BookingStartOn = TimeOf(r, "StartOn"),
                        BookingEndOn = TimeOf(r, "EndOn"),
                        Status = Str(r, "Status"),
                        BookingCount = Int(r, "BookingCount")
                    }).ToList();
                    break;
                }
            }
            return ApiResponse.Ok(dto);

            static IDictionary<string, object?>? Head(List<List<IDictionary<string, object?>>> s) => s.Count > 0 ? s[0].FirstOrDefault() : null;
            static List<IDictionary<string, object?>> Rows(List<List<IDictionary<string, object?>>> s) => s.Count > 1 ? s[1] : new();
            static object? Val(IDictionary<string, object?>? r, string key) => r != null && r.TryGetValue(key, out var v) ? v : null;
            static int Int(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToInt32(v) : 0;
            static int? IntOrNull(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToInt32(v) : null;
            static decimal Dec(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToDecimal(v) : 0m;
            static bool Bool(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v && Convert.ToBoolean(v);
            static string? Str(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToString(v) : null;
            static string? DayOf(IDictionary<string, object?>? r, string key) => Val(r, key) is DateTime d ? d.ToString("yyyy-MM-dd") : null;
            static string? TimeOf(IDictionary<string, object?>? r, string key) => Val(r, key) is DateTime d ? d.ToString("yyyy-MM-ddTHH:mm:ss") : null;
            static decimal Money(decimal v) => Math.Round(v, 2);
            static string Day(DateTime d) => d.ToString("yyyy-MM-dd");
        }

        /// <summary>Start of this calendar month / quarter / year (as the overview's period).</summary>
        private static DateTime PeriodStart(string period, DateTime today) => period switch
        {
            "year" => new DateTime(today.Year, 1, 1),
            "quarter" => new DateTime(today.Year, (today.Month - 1) / 3 * 3 + 1, 1),
            _ => new DateTime(today.Year, today.Month, 1)
        };

        private static bool TryDay(string? value, out DateTime day) =>
            DateTime.TryParseExact((value ?? "").Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out day);

        private static bool TryMonth(string? value, out DateTime month)
        {
            var ok = DateTime.TryParseExact((value ?? "").Trim(), "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out month);
            if (ok) month = new DateTime(month.Year, month.Month, 1);
            return ok;
        }
    }
}
