namespace WorkNest.Application.Services
{
    /// <summary>
    /// Recurring billing-period arithmetic that never drifts at month end.
    ///
    /// Rule
    /// ----
    /// Periods are contiguous: the next period always starts the day after the last invoiced
    /// period end (nextStart = lastPeriodEnd + 1 day). The next period END is anchored to a fixed
    /// "anchor day" instead of being chained from the previous end:
    ///
    ///     nextEnd = AnchorAdd(nextStart, months, anchorDay) - 1 day
    ///
    /// where AnchorAdd moves <c>months</c> calendar months forward and uses
    /// min(anchorDay, days in target month). Because the anchor day is re-applied every time
    /// (rather than taking the already-clamped day of the previous period), a short month only
    /// shortens that one period; the following month returns to the original day.
    ///
    /// Anchor day
    /// ----------
    /// * If nextStart lies on the contract-start grid, i.e. nextStart == AnchorAdd(StartOn, m) for
    ///   some m (same day as StartOn, or the clamped last day of a shorter month), the anchor day is
    ///   StartOn.Day. This is the un-prorated case: period k = [AnchorAdd(StartOn, k*n),
    ///   AnchorAdd(StartOn, (k+1)*n) - 1 day].
    /// * Otherwise (e.g. the first invoice was prorated by InvoiceCalculationEngine and ended on a
    ///   month end, so recurring periods start on the 1st) the anchor day is nextStart.Day.
    ///
    /// Examples (non-leap year unless stated; first invoice not prorated):
    ///   31 Jan, monthly   : 31 Jan-27 Feb, 28 Feb-30 Mar, 31 Mar-29 Apr, 30 Apr-30 May, 31 May-29 Jun, 30 Jun-30 Jul
    ///   31 Jan, quarterly : 31 Jan-29 Apr, 30 Apr-30 Jul, 31 Jul-30 Oct, 31 Oct-30 Jan
    ///   15 Mar, monthly   : 15 Mar-14 Apr, 15 Apr-14 May, 15 May-14 Jun ...
    ///   (prorated first invoice ending 31 Mar -> next periods 1 Apr-30 Apr, 1 May-31 May ...)
    ///   29 Feb 2028, monthly : 29 Feb-28 Mar, 29 Mar-28 Apr, ... 29 Jan 2029-27 Feb 2029, 28 Feb 2029-28 Mar 2029
    /// </summary>
    public static class BillingPeriods
    {
        /// <summary>
        /// Adds <paramref name="months"/> to <paramref name="from"/>'s month and places the result on
        /// <paramref name="anchorDay"/>, clamped to the last day of the target month.
        /// </summary>
        public static DateTime AnchorAdd(DateTime from, int months, int anchorDay)
        {
            var firstOfTarget = new DateTime(from.Year, from.Month, 1).AddMonths(months);
            int day = Math.Min(Math.Max(1, anchorDay), DateTime.DaysInMonth(firstOfTarget.Year, firstOfTarget.Month));
            return firstOfTarget.AddDays(day - 1);
        }

        /// <summary>AnchorAdd anchored to the contract start's own day (period boundary k*months from start).</summary>
        public static DateTime AnchorAdd(DateTime contractStart, int months) =>
            AnchorAdd(contractStart.Date, months, contractStart.Day);

        /// <summary>True when <paramref name="date"/> equals AnchorAdd(contractStart, m) for some m &gt;= 0.</summary>
        public static bool IsOnContractGrid(DateTime contractStart, DateTime date)
        {
            if (date.Date < contractStart.Date) return false;
            if (date.Day == contractStart.Day) return true;
            // Clamped boundary: last day of a month shorter than the contract-start day.
            return date.Day == DateTime.DaysInMonth(date.Year, date.Month) && contractStart.Day > date.Day;
        }

        /// <summary>
        /// Anchor day for a recurring period starting on <paramref name="periodStart"/>
        /// (see class remarks).
        /// </summary>
        public static int ResolveAnchorDay(DateTime? contractStart, DateTime periodStart)
        {
            if (contractStart.HasValue && IsOnContractGrid(contractStart.Value, periodStart))
                return contractStart.Value.Day;
            return periodStart.Day;
        }

        /// <summary>
        /// Inclusive end date of a <paramref name="months"/>-month period starting on
        /// <paramref name="periodStart"/>, anchored to the contract start (no month-end drift).
        /// </summary>
        /// <summary>
        /// Length of the inclusive period <paramref name="start"/>..<paramref name="endInclusive"/> in months: whole
        /// months (anchored to the start day) plus the remaining days as a fraction of that month. A full 3-month
        /// cycle gives 3; Oct 1 - Nov 15 gives 1.5. Same rule as WN_Invoice_CreateRecurring (last partial cycle).
        /// </summary>
        public static decimal MonthsBetween(DateTime start, DateTime endInclusive)
        {
            start = start.Date; endInclusive = endInclusive.Date;
            if (endInclusive < start) return 0m;
            int m = 0;
            while (AnchorAdd(start, m + 1, start.Day).AddDays(-1) <= endInclusive) m++;
            var restStart = AnchorAdd(start, m, start.Day);
            if (restStart > endInclusive) return m;
            int restDays = (endInclusive - restStart).Days + 1;
            int dim = DateTime.DaysInMonth(restStart.Year, restStart.Month);
            return Math.Round(m + (decimal)restDays / dim, 6);
        }

        public static DateTime PeriodEnd(DateTime? contractStart, DateTime periodStart, int months)
        {
            if (months <= 0) months = 1;
            int anchorDay = ResolveAnchorDay(contractStart, periodStart.Date);
            return AnchorAdd(periodStart.Date, months, anchorDay).AddDays(-1);
        }

        /// <summary>
        /// Next contiguous period after <paramref name="lastPeriodEnd"/> (or the first one from the
        /// contract start when nothing has been invoiced yet).
        /// </summary>
        public static (DateTime Start, DateTime End) NextPeriod(DateTime? contractStart, DateTime? lastPeriodEnd, int months, DateTime fallbackStart)
        {
            DateTime start = lastPeriodEnd.HasValue
                ? lastPeriodEnd.Value.Date.AddDays(1)
                : (contractStart?.Date ?? fallbackStart.Date);
            return (start, PeriodEnd(contractStart, start, months));
        }
    }
}
