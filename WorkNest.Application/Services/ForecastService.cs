using System.Globalization;
using WorkNest.Application.DTOs.Reports;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Revenue &amp; occupancy forecast for the next N calendar months (default 3, at most 12), starting with the
    /// month after the current one, built only from leases already in the system (live bookings 1, 2, 5, 33, not
    /// deleted, on active spaces). Rules:
    /// - Expected rent: each lease's monthly rent (less a percentage discount; no tax / service charge), prorated by
    ///   the days it is active in the month on a 30-day month (InvoiceCalculationEngine.ProrationDaysPerMonth).
    ///   This is the rent the leases earn in the month, whether or not it has been invoiced yet; invoices are never
    ///   added on top, so an advance invoice already issued for the month is not counted twice.
    ///   ConfirmedRent is the part from running leases (2, 33); the rest is from bookings waiting for confirmation.
    /// - Occupancy: distinct spaces with a live lease on the month's last day / active spaces now.
    /// - Leases ending: running leases whose end date is in the month (no renewal assumed).
    ///   Leases starting: live leases whose start date is in the month; PendingStarting = those not confirmed yet.
    /// </summary>
    public class ForecastService : IForecastService
    {
        public const int DefaultMonths = 3;
        public const int MaxMonths = 12;

        private readonly IReportRepository _repo;
        private readonly IBusinessClock _clock;

        public ForecastService(IReportRepository repo, IBusinessClock clock)
        {
            _repo = repo;
            _clock = clock;
        }

        private static object? Val(IDictionary<string, object?> r, string key) => r.TryGetValue(key, out var v) ? v : null;
        private static int Int(IDictionary<string, object?> r, string key) => Val(r, key) is { } v ? Convert.ToInt32(v) : 0;
        private static decimal Dec(IDictionary<string, object?> r, string key) => Val(r, key) is { } v ? Convert.ToDecimal(v) : 0m;
        private static decimal Pct(int part, int total) => total > 0 ? Math.Round(part * 100m / total, 1) : 0m;

        public async Task<ForecastDto> GetAsync(IReadOnlyCollection<int>? locationIds, int months = DefaultMonths)
        {
            months = months <= 0 ? DefaultMonths : Math.Min(months, MaxMonths);
            var now = _clock.Now;
            var first = new DateTime(now.Year, now.Month, 1).AddMonths(1);

            var sets = await _repo.GetForecastAsync(locationIds, first, months);
            var rows = sets.Count > 0 ? sets[0] : new();

            ForecastMonthDto Month(int n, IEnumerable<IDictionary<string, object?>> group)
            {
                var g = group.ToList();
                var start = first.AddMonths(n);
                var m = new ForecastMonthDto
                {
                    Month = start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    Label = start.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                    ExpectedRent = Math.Round(g.Sum(r => Dec(r, "ExpectedRent")), 0),
                    ConfirmedRent = Math.Round(g.Sum(r => Dec(r, "ConfirmedRent")), 0),
                    TotalSpaces = g.Sum(r => Int(r, "TotalSpaces")),
                    OccupiedSpaces = g.Sum(r => Int(r, "OccupiedSpaces")),
                    LeasesEnding = g.Sum(r => Int(r, "LeasesEnding")),
                    LeasesStarting = g.Sum(r => Int(r, "LeasesStarting")),
                    PendingStarting = g.Sum(r => Int(r, "PendingStarting"))
                };
                m.OccupancyPct = Pct(m.OccupiedSpaces, m.TotalSpaces);
                return m;
            }

            var dto = new ForecastDto
            {
                Months = months,
                FromMonth = first.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                GeneratedAt = now.ToString("yyyy-MM-ddTHH:mm:ss"),
                Totals = Enumerable.Range(0, months).Select(n => Month(n, rows.Where(r => Int(r, "n") == n))).ToList()
            };

            // Per location only when more than one (real) location is in scope; spaces without a location
            // (LocationId 0) still count in the totals.
            var locations = rows.Where(r => Int(r, "LocationId") > 0)
                .GroupBy(r => Int(r, "LocationId"))
                .Select(g => new ForecastLocationDto
                {
                    LocationId = g.Key,
                    Name = Convert.ToString(Val(g.First(), "LocationName")) is { Length: > 0 } name ? name : $"Location #{g.Key}",
                    Months = Enumerable.Range(0, months).Select(n => Month(n, g.Where(r => Int(r, "n") == n))).ToList()
                })
                .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (locations.Count > 1) dto.Locations = locations;
            return dto;
        }
    }
}
