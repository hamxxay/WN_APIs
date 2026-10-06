namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// The one business clock (Pakistan time). Use it for every business date: due dates, "today" comparisons,
    /// invoice dates, billing periods, job "today", agreement dates, proration and dashboard periods.
    /// Audit timestamps that are intentionally UTC (CreatedOn = SYSUTCDATETIME etc.) do not use it.
    /// </summary>
    public interface IBusinessClock
    {
        /// <summary>Current business date (Pakistan), time 00:00, Kind Unspecified.</summary>
        DateTime Today { get; }

        /// <summary>Current business wall-clock time (Pakistan), Kind Unspecified.</summary>
        DateTime Now { get; }

        /// <summary>The business time zone.</summary>
        TimeZoneInfo TimeZone { get; }

        /// <summary>Converts a UTC instant to business wall-clock time.</summary>
        DateTime FromUtc(DateTime utc);
    }
}
