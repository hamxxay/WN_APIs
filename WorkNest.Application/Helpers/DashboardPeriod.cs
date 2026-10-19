using System.Globalization;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// The period of the CEO and Sales Manager dashboards (CS_01): month | quarter | year (calendar period to date, as
    /// before) or custom (any day range, both days included, at most <see cref="MaxCustomDays"/> days, not after today).
    /// Previous period of a custom range = the same number of days immediately before it (1-15 Oct -> 16-30 Sep). The
    /// calendar periods keep their own rules inside the procedures (start of the month / quarter / year, previous =
    /// the same number of days one period earlier); only a custom range is sent to them as @From / @To.
    /// </summary>
    public sealed class DashboardPeriod
    {
        /// <summary>Longest custom range, in days (a leap year).</summary>
        public const int MaxCustomDays = 366;

        /// <summary>No custom range starts before this day.</summary>
        public static readonly DateTime Earliest = new(2000, 1, 1);

        public const string PeriodError = "period must be month, quarter, year or custom.";

        private static readonly CultureInfo En = CultureInfo.InvariantCulture;

        private DashboardPeriod(string key, int months, DateTime? from, DateTime? to)
        {
            Key = key;
            Months = months;
            From = from;
            To = to;
        }

        /// <summary>month | quarter | year | custom</summary>
        public string Key { get; }

        /// <summary>1, 3 or 12 for the calendar periods (the procedures' @PMonths); 1 for custom (not used then).</summary>
        public int Months { get; }

        /// <summary>Custom: the first day of the range; null for the calendar periods.</summary>
        public DateTime? From { get; }

        /// <summary>Custom: the last day of the range (included); null for the calendar periods.</summary>
        public DateTime? To { get; }

        public bool IsCustom => From != null && To != null;

        /// <summary>Custom: days in the range (both days included); 0 for the calendar periods.</summary>
        public int Days => IsCustom ? (To!.Value - From!.Value).Days + 1 : 0;

        /// <summary>Custom: first day of the previous range (as many days, just before <see cref="From"/>).</summary>
        public DateTime? PreviousFrom => IsCustom ? From!.Value.AddDays(-Days) : null;

        /// <summary>Custom: last day of the previous range (the day before <see cref="From"/>).</summary>
        public DateTime? PreviousTo => IsCustom ? From!.Value.AddDays(-1) : null;

        /// <summary>Custom: the range as yyyy-MM-dd strings (the DTOs' from / to); null for the calendar periods.</summary>
        public string? FromText => From?.ToString("yyyy-MM-dd", En);
        public string? ToText => To?.ToString("yyyy-MM-dd", En);
        public string? PreviousFromText => PreviousFrom?.ToString("yyyy-MM-dd", En);
        public string? PreviousToText => PreviousTo?.ToString("yyyy-MM-dd", En);

        /// <summary>
        /// Reads period (default month) and, for custom, from / to (yyyy-MM-dd, both required). A custom range must start
        /// on or before it ends, be at most <see cref="MaxCustomDays"/> days long, start no earlier than
        /// <see cref="Earliest"/> and end no later than <paramref name="today"/>. from / to are ignored for the calendar
        /// periods (the drill-down lists use their own from / to as a window).
        /// </summary>
        public static bool TryParse(string? period, string? from, string? to, DateTime today, out DashboardPeriod result, out string? error)
        {
            result = new DashboardPeriod("month", 1, null, null);
            error = null;
            var key = string.IsNullOrWhiteSpace(period) ? "month" : period.Trim().ToLowerInvariant();
            switch (key)
            {
                case "month": return true;
                case "quarter": result = new DashboardPeriod(key, 3, null, null); return true;
                case "year": result = new DashboardPeriod(key, 12, null, null); return true;
                case "custom": break;
                default: error = PeriodError; return false;
            }

            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            {
                error = "A custom period needs from and to (yyyy-MM-dd).";
                return false;
            }
            if (!DateTime.TryParseExact(from.Trim(), "yyyy-MM-dd", En, DateTimeStyles.None, out var f))
            {
                error = "from must be yyyy-MM-dd.";
                return false;
            }
            if (!DateTime.TryParseExact(to.Trim(), "yyyy-MM-dd", En, DateTimeStyles.None, out var t))
            {
                error = "to must be yyyy-MM-dd.";
                return false;
            }
            if (f > t) { error = "from must be on or before to."; return false; }
            if (f < Earliest) { error = $"from cannot be before {Earliest:yyyy-MM-dd}."; return false; }
            if (t > today.Date) { error = "to cannot be after today."; return false; }
            if ((t - f).Days + 1 > MaxCustomDays) { error = $"A custom period can be at most {MaxCustomDays} days."; return false; }
            result = new DashboardPeriod(key, 1, f, t);
            return true;
        }

        /// <summary>
        /// "October 2026", "Q4 2026", "2026" for the calendar periods (as of <paramref name="now"/>); a custom range as
        /// "15 Oct 2026" (one day), "October 2026" (a whole month), "1 Oct - 15 Oct 2026" or "20 Dec 2025 - 10 Jan 2026".
        /// </summary>
        public string Label(DateTime now)
        {
            if (!IsCustom)
            {
                return Key switch
                {
                    "year" => now.Year.ToString(En),
                    "quarter" => $"Q{(now.Month - 1) / 3 + 1} {now.Year}",
                    _ => now.ToString("MMMM yyyy", En)
                };
            }
            return RangeLabel(From!.Value, To!.Value);
        }

        /// <summary>A day range in words (see <see cref="Label"/>).</summary>
        public static string RangeLabel(DateTime from, DateTime to)
        {
            if (from.Date == to.Date) return from.ToString("d MMM yyyy", En);
            if (from.Day == 1 && to.Date == from.AddMonths(1).AddDays(-1).Date) return from.ToString("MMMM yyyy", En);
            return from.Year == to.Year
                ? $"{from.ToString("d MMM", En)} - {to.ToString("d MMM yyyy", En)}"
                : $"{from.ToString("d MMM yyyy", En)} - {to.ToString("d MMM yyyy", En)}";
        }

        /// <summary>The range for a records title: "between 1 Oct 2026 and 15 Oct 2026" (custom only).</summary>
        public string BetweenWords() =>
            IsCustom ? $"between {From!.Value.ToString("d MMM yyyy", En)} and {To!.Value.ToString("d MMM yyyy", En)}" : "";
    }
}
