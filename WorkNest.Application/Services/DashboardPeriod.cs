using System.Globalization;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// The period of the admin dashboard figures (api/dashboard/overview, api/dashboard/role, api/dashboard/drill/*):
    /// this month / quarter / year to date (calendar, by the business date), as before, or a custom range of whole days
    /// (period=custom&amp;from=yyyy-MM-dd&amp;to=yyyy-MM-dd, both days included, at most <see cref="MaxCustomDays"/> days).
    /// A missing or unknown period is "month" (as before); a custom range with missing or bad dates is refused.
    /// </summary>
    public sealed class DashboardPeriod
    {
        /// <summary>Longest custom range: two years (the year view's 24-month trend covers the same).</summary>
        public const int MaxCustomDays = 731;

        private static readonly DateTime Earliest = new(2000, 1, 1);
        private static readonly DateTime Latest = new(2100, 12, 31);
        private static readonly CultureInfo En = CultureInfo.InvariantCulture;

        /// <summary>month | quarter | year | custom</summary>
        public string Period { get; }
        /// <summary>First day of the period.</summary>
        public DateTime Start { get; }
        /// <summary>The day after the last day of a custom range; null = to date (the calendar periods have no end).</summary>
        public DateTime? EndExclusive { get; }
        /// <summary>1 / 3 / 12 for the calendar periods (the overview's existing rules), 0 for a custom range.</summary>
        public int Months { get; }

        public bool IsCustom => Period == "custom";
        /// <summary>Custom range: its first day; null for the calendar periods.</summary>
        public DateTime? From => IsCustom ? Start : null;
        /// <summary>Custom range: its last day (included); null for the calendar periods.</summary>
        public DateTime? To => IsCustom ? EndExclusive!.Value.AddDays(-1) : null;
        /// <summary>Custom range: its length in days (both ends included); 0 for the calendar periods.</summary>
        public int Days => IsCustom ? (EndExclusive!.Value - Start).Days : 0;

        /// <summary>
        /// Months the trend charts show: 6 / 12 / 24 for month / quarter / year (as before); a custom range: every
        /// calendar month it touches (at most 25 for a two-year range).
        /// </summary>
        public int SeriesMonths => Period switch
        {
            "year" => 24,
            "quarter" => 12,
            "custom" => (To!.Value.Year - Start.Year) * 12 + To.Value.Month - Start.Month + 1,
            _ => 6
        };

        /// <summary>Months the lease expiry chart looks ahead from today: 3 / 6 / 12 (as before); a custom range: 3, as a month.</summary>
        public int AheadMonths => Period switch { "year" => 12, "quarter" => 6, _ => 3 };

        private DashboardPeriod(string period, DateTime start, DateTime? endExclusive, int months)
        {
            Period = period;
            Start = start;
            EndExclusive = endExclusive;
            Months = months;
        }

        /// <summary>This month / quarter / year to date by the business date <paramref name="today"/>.</summary>
        public static DashboardPeriod Calendar(string? period, DateTime today)
        {
            var p = (period ?? "month").Trim().ToLowerInvariant();
            var d = today.Date;
            return p switch
            {
                "year" => new DashboardPeriod("year", new DateTime(d.Year, 1, 1), null, 12),
                "quarter" => new DashboardPeriod("quarter", new DateTime(d.Year, (d.Month - 1) / 3 * 3 + 1, 1), null, 3),
                _ => new DashboardPeriod("month", new DateTime(d.Year, d.Month, 1), null, 1)
            };
        }

        /// <summary>
        /// Reads the period query. "custom" needs from and to (yyyy-MM-dd), from on or before to, at most
        /// <see cref="MaxCustomDays"/> days, between 2000 and 2100; otherwise false and a message for the user.
        /// Any other value: the calendar period (unknown = month), as the dashboard always did.
        /// </summary>
        public static bool TryParse(string? period, string? from, string? to, DateTime today, out DashboardPeriod result, out string? error)
        {
            error = null;
            var p = (period ?? "").Trim().ToLowerInvariant();
            if (p != "custom")
            {
                result = Calendar(p, today);
                return true;
            }

            result = Calendar("month", today);
            if (!TryDay(from, out var f) || !TryDay(to, out var t))
            {
                error = "Choose both dates of the custom range (from and to).";
                return false;
            }
            if (f > t)
            {
                error = "The custom range must start on or before its end date.";
                return false;
            }
            if (f < Earliest || t > Latest)
            {
                error = "Choose dates between 2000 and 2100.";
                return false;
            }
            if ((t - f).Days + 1 > MaxCustomDays)
            {
                error = $"A custom range can be at most {MaxCustomDays} days (two years).";
                return false;
            }
            result = new DashboardPeriod("custom", f, t.AddDays(1), 0);
            return true;
        }

        private static bool TryDay(string? value, out DateTime day)
        {
            day = default;
            return !string.IsNullOrWhiteSpace(value)
                && DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", En, DateTimeStyles.None, out day);
        }

        /// <summary>"This month", "This quarter", "This year" or "1 Jan 2026 to 31 Mar 2026".</summary>
        public string Label => Period switch
        {
            "year" => "This year",
            "quarter" => "This quarter",
            "custom" => $"{Start.ToString("d MMM yyyy", En)} to {To!.Value.ToString("d MMM yyyy", En)}",
            _ => "This month"
        };
    }
}
