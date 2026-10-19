using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Main admin dashboard (/admin/dashboard): the Space Types Capacity &amp; Occupancy cards, the lists behind their tags
    /// and the Space Operations &amp; Inventory table. Counted by the dbo.WN_Dash_* procedures (DASH_01) for every space,
    /// booking and quotation, on the rules the page used in the browser (see dbo.WN_Dash_SpaceStates), with the business
    /// clock for "now". The caller passes the locations already limited to what the user may see.
    /// </summary>
    public class MainDashboardService : IMainDashboardService
    {
        public const int DefaultExpiringDays = 30;
        public const int MaxExpiringDays = 365;
        public const int MaxDetailsPageSize = 200;
        public const int MaxInventoryPageSize = 100;
        private const int MaxTextLength = 200;

        public static readonly string[] Tags = { "booked", "vacant", "quoted", "expiring" };
        public static readonly string[] Statuses = { "Booked", "Quoted", "Available" };
        public static readonly string[] SortFields = { "urgency", "spaceCode", "customer", "bookingStart", "bookingEnd", "remainingTime", "status" };

        private readonly IMainDashboardRepository _repo;
        private readonly IBusinessClock _clock;

        public MainDashboardService(IMainDashboardRepository repo, IBusinessClock clock)
        {
            _repo = repo;
            _clock = clock;
        }

        public async Task<ApiResponse> GetSummaryAsync(IReadOnlyCollection<int>? locationIds, int? expiringDays)
        {
            if (!TryDays(expiringDays, out var days, out var error)) return ApiResponse.Fail(error!);
            var now = _clock.Now;
            var s = await _repo.GetSpaceTypeSummaryAsync(locationIds, days, now);
            var cards = Set(s, 0).Select(r => new SpaceTypeCardDto
            {
                SpaceTypeId = Int(r, "SpaceTypeId"),
                SpaceTypeName = Str(r, "SpaceType") ?? "Other",
                LocationId = IntOrNull(r, "LocationId"),
                LocationName = Str(r, "LocationName"),
                TotalSpaces = Int(r, "TotalSpaces"),
                BookedCount = Int(r, "BookedCount"),
                VacantCount = Int(r, "VacantCount"),
                QuotedCount = Int(r, "QuotedCount"),
                ExpiringCount = Int(r, "ExpiringCount")
            }).ToList();
            return ApiResponse.Ok(new MainDashboardSummaryDto
            {
                ExpiringDays = days,
                TotalSpaces = cards.Sum(c => c.TotalSpaces),
                Cards = cards,
                GeneratedAt = now.ToString("yyyy-MM-ddTHH:mm:ss")
            });
        }

        public async Task<ApiResponse> GetDetailsAsync(IReadOnlyCollection<int>? locationIds, int spaceTypeId, string? tag, int? expiringDays,
            int page, int pageSize)
        {
            if (spaceTypeId <= 0) return ApiResponse.Fail("Choose a space type.");
            var t = (tag ?? "").Trim().ToLowerInvariant();
            if (!Tags.Contains(t)) return ApiResponse.Fail("Unknown list: use booked, vacant, quoted or expiring.");
            if (!TryDays(expiringDays, out var days, out var error)) return ApiResponse.Fail(error!);
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize <= 0 ? 25 : pageSize, 1, MaxDetailsPageSize);

            var s = await _repo.GetSpaceTypeDetailsAsync(locationIds, spaceTypeId, t, days, page, pageSize, _clock.Now);
            var items = Set(s, 1).Select(r => new SpaceTypeDetailRowDto
            {
                SpaceId = Int(r, "SpaceId"),
                SpaceCode = Str(r, "SpaceCode") ?? "",
                SpaceName = Str(r, "SpaceName") ?? "",
                Capacity = Int(r, "Capacity"),
                LocationName = Str(r, "LocationName"),
                BookingId = IntOrNull(r, "BookingId"),
                BookingStart = Time(r, "BookingStart"),
                BookingEnd = Time(r, "BookingEnd"),
                BookingStatus = Str(r, "BookingStatus"),
                CustomerName = t == "quoted" ? Str(r, "QuotationCustomer") : Str(r, "CustomerName"),
                CompanyName = t == "quoted" ? Str(r, "QuotationCompany") : Str(r, "CompanyName"),
                Has247 = Bool(r, "Has247"),
                HasMorning = Bool(r, "HasMorning"),
                HasEvening = Bool(r, "HasEvening"),
                NextBookingDate = Time(r, "NextBookingStart"),
                NextBookingCustomer = Str(r, "NextBookingCustomer"),
                QuotationId = IntOrNull(r, "QuotationId"),
                QuotationNumber = Str(r, "QuotationNumber"),
                QuotationStatus = Str(r, "QuotationStatus"),
                QuotationDate = Day(r, "QuotationDate"),
                QuotationExpiryDate = Day(r, "QuotationExpiry"),
                QuotationAmount = DecOrNull(r, "QuotationAmount"),
                IsAlsoBooked = Bool(r, "IsBooked"),
                DaysRemaining = IntOrNull(r, "DaysRemaining")
            }).ToList();
            return ApiResponse.Ok(new SpaceTypeDetailsDto
            {
                Tag = t,
                SpaceTypeId = spaceTypeId,
                Total = Math.Max(Int(Set(s, 0).FirstOrDefault(), "Total"), items.Count),
                Page = page,
                PageSize = pageSize,
                Items = items
            });
        }

        public async Task<ApiResponse> GetInventoryAsync(IReadOnlyCollection<int>? locationIds, MainDashboardInventoryQuery query)
        {
            query ??= new MainDashboardInventoryQuery();
            if (!TryDays(query.ExpiringDays, out var days, out var error)) return ApiResponse.Fail(error!);

            string? status = null;
            if (!string.IsNullOrWhiteSpace(query.Status) && !query.Status.Trim().Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                status = Statuses.FirstOrDefault(x => x.Equals(query.Status.Trim(), StringComparison.OrdinalIgnoreCase));
                if (status is null) return ApiResponse.Fail("Unknown status: use Booked, Quoted or Available.");
            }
            var sortBy = string.IsNullOrWhiteSpace(query.SortBy) ? "urgency"
                : SortFields.FirstOrDefault(x => x.Equals(query.SortBy.Trim(), StringComparison.OrdinalIgnoreCase));
            if (sortBy is null) return ApiResponse.Fail("Unknown sort column.");
            var dir = (query.SortDir ?? "asc").Trim().ToLowerInvariant();
            if (dir is not ("asc" or "desc")) return ApiResponse.Fail("Sort direction must be asc or desc.");
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize <= 0 ? 10 : query.PageSize, 1, MaxInventoryPageSize);
            var spaceType = Text(query.SpaceType);
            if (spaceType is not null && spaceType.Equals("ALL", StringComparison.OrdinalIgnoreCase)) spaceType = null;

            var s = await _repo.GetInventoryAsync(locationIds, query.Location is > 0 ? query.Location : null, Text(query.Search),
                Text(query.Customer), spaceType, status, query.PrivateOnly, days, sortBy, dir == "desc", page, pageSize, _clock.Now);
            var items = Set(s, 1).Select(r => new InventoryRowDto
            {
                SpaceId = Int(r, "SpaceId"),
                SpaceCode = Str(r, "SpaceCode") ?? "",
                SpaceName = Str(r, "SpaceName") ?? "",
                Capacity = Int(r, "Capacity"),
                SpaceTypeId = Int(r, "SpaceTypeId"),
                SpaceTypeName = Str(r, "SpaceTypeName") ?? "Other",
                LocationId = IntOrNull(r, "LocationId"),
                LocationName = Str(r, "LocationName"),
                Status = Str(r, "Status") ?? "Available",
                BookingId = IntOrNull(r, "BookingId"),
                CustomerName = Str(r, "CustomerName"),
                CompanyName = Str(r, "CompanyName"),
                BookingStart = Time(r, "BookingStart"),
                BookingEnd = Time(r, "BookingEnd"),
                BookingStatus = Str(r, "BookingStatus"),
                ShiftType = Str(r, "ShiftType"),
                QuotationId = IntOrNull(r, "QuotationId"),
                QuotationNumber = Str(r, "QuotationNumber"),
                QuotationCustomerName = Str(r, "QuotationCustomerName"),
                QuotationDate = Day(r, "QuotationDate"),
                QuotationExpiryDate = Day(r, "QuotationExpiryDate"),
                QuotationAmount = DecOrNull(r, "QuotationAmount"),
                QuotationStatus = Str(r, "QuotationStatus"),
                DaysRemaining = IntOrNull(r, "DaysRemaining") ?? 99999,
                IsExpiringSoon = Bool(r, "IsExpiringSoon"),
                IsPrivateSpace = Bool(r, "IsPrivate")
            }).ToList();
            return ApiResponse.Ok(new InventoryPageDto
            {
                Total = Math.Max(Int(Set(s, 0).FirstOrDefault(), "Total"), items.Count),
                Page = page,
                PageSize = pageSize,
                Items = items
            });
        }

        // ---------- helpers ----------

        /// <summary>Expiring Soon window: 30 days when not given, otherwise 1 to 365.</summary>
        public static bool TryDays(int? value, out int days, out string? error)
        {
            days = value ?? DefaultExpiringDays;
            error = null;
            if (days >= 1 && days <= MaxExpiringDays) return true;
            error = $"The expiry threshold must be between 1 and {MaxExpiringDays} days.";
            return false;
        }

        /// <summary>Trimmed text, at most 200 characters; null when empty.</summary>
        private static string? Text(string? value)
        {
            var t = value?.Trim();
            if (string.IsNullOrEmpty(t)) return null;
            return t.Length > MaxTextLength ? t[..MaxTextLength] : t;
        }

        private static List<IDictionary<string, object?>> Set(List<List<IDictionary<string, object?>>> s, int i) => i < s.Count ? s[i] : new();
        private static object? Val(IDictionary<string, object?>? r, string key) => r != null && r.TryGetValue(key, out var v) ? v : null;
        private static int Int(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToInt32(v) : 0;
        private static int? IntOrNull(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToInt32(v) : null;
        private static decimal? DecOrNull(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Math.Round(Convert.ToDecimal(v), 2) : null;
        private static string? Str(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToString(v) : null;
        private static bool Bool(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v && Convert.ToBoolean(v);
        private static string? Time(IDictionary<string, object?>? r, string key) => Val(r, key) is DateTime d ? d.ToString("yyyy-MM-ddTHH:mm:ss") : null;
        private static string? Day(IDictionary<string, object?>? r, string key) => Val(r, key) is DateTime d ? d.ToString("yyyy-MM-dd") : null;
    }
}
