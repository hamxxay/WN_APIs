namespace WorkNest.Application.DTOs.Reports
{
    /// <summary>
    /// Revenue &amp; occupancy forecast for the next N calendar months, from the leases already in the system
    /// (see ForecastService for the rules). Totals cover every location in scope; Locations is filled only when
    /// more than one location is in scope.
    /// </summary>
    public class ForecastDto
    {
        public int Months { get; set; }
        /// <summary>First forecast month, yyyy-MM (the month after the current one).</summary>
        public string FromMonth { get; set; } = "";
        public List<ForecastMonthDto> Totals { get; set; } = new();
        public List<ForecastLocationDto> Locations { get; set; } = new();
        public string GeneratedAt { get; set; } = "";
    }

    public class ForecastMonthDto
    {
        /// <summary>yyyy-MM</summary>
        public string Month { get; set; } = "";
        /// <summary>e.g. "Nov 2026"</summary>
        public string Label { get; set; } = "";
        /// <summary>Monthly rent of every live lease (confirmed + waiting for confirmation), prorated by days active.</summary>
        public decimal ExpectedRent { get; set; }
        /// <summary>The part of ExpectedRent from confirmed (running) leases only.</summary>
        public decimal ConfirmedRent { get; set; }
        public int TotalSpaces { get; set; }
        /// <summary>Distinct spaces with a live lease on the last day of the month.</summary>
        public int OccupiedSpaces { get; set; }
        public decimal OccupancyPct { get; set; }
        /// <summary>Running leases whose end date falls in the month.</summary>
        public int LeasesEnding { get; set; }
        /// <summary>Live leases whose start date falls in the month.</summary>
        public int LeasesStarting { get; set; }
        /// <summary>The part of LeasesStarting still waiting for confirmation.</summary>
        public int PendingStarting { get; set; }
    }

    public class ForecastLocationDto
    {
        public int LocationId { get; set; }
        public string Name { get; set; } = "";
        public List<ForecastMonthDto> Months { get; set; } = new();
    }

    /// <summary>Settings for the weekly email (config section "Reports:Weekly").</summary>
    public class WeeklyReportOptions
    {
        public bool Enabled { get; set; } = true;
        public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;
        /// <summary>Hour of the day (Pakistan time) from which the report is sent.</summary>
        public int Hour { get; set; } = 9;
        public List<string> ExtraRecipients { get; set; } = new();
    }

    /// <summary>The weekly report as built: its numbers, plus the e-mail subject and HTML.</summary>
    public class WeeklyReportDto
    {
        /// <summary>ISO week of the report, e.g. "2026-W42".</summary>
        public string PeriodKey { get; set; } = "";
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<WeeklyLocationRowDto> Locations { get; set; } = new();
        public WeeklyLocationRowDto Total { get; set; } = new() { Name = "Company total" };
        public int NewInquiries { get; set; }
        /// <summary>Running leases ending in the next 60 days, soonest first (at most 10).</summary>
        public List<ExpiringLeaseDto> ExpiringLeases { get; set; } = new();
        public ForecastDto Forecast { get; set; } = new();
        public string Subject { get; set; } = "";
        public string Html { get; set; } = "";
    }

    public class WeeklyLocationRowDto
    {
        public int LocationId { get; set; }
        public string Name { get; set; } = "";
        public int TotalSpaces { get; set; }
        public int OccupiedSpaces { get; set; }
        public decimal OccupancyPct { get; set; }
        public int ActiveBookings { get; set; }
        public decimal Invoiced { get; set; }
        public decimal Collected { get; set; }
        public decimal Outstanding { get; set; }
        public decimal Overdue { get; set; }
        public int Expiring30 { get; set; }
        public int Expiring60 { get; set; }
    }

    public class ExpiringLeaseDto
    {
        public int BookingId { get; set; }
        public string? Customer { get; set; }
        public string? Space { get; set; }
        public string? Location { get; set; }
        public DateTime EndOn { get; set; }
        public int DaysLeft { get; set; }
        public decimal MonthlyRent { get; set; }
    }

    public class WeeklyReportSendResultDto
    {
        public bool IsSuccessful { get; set; }
        public string Message { get; set; } = "";
        /// <summary>Addresses the report was sent to.</summary>
        public List<string> Recipients { get; set; } = new();
        /// <summary>Addresses it could not be sent to.</summary>
        public List<string> Failed { get; set; } = new();
        /// <summary>Sent | Partial | Failed (stored in WN_ScheduledReportRuns.Status).</summary>
        public string Status { get; set; } = "";
        public string? Error { get; set; }
    }

    /// <summary>Outcome of claiming a scheduled report run in WN_ScheduledReportRuns.</summary>
    public enum ReportClaimResult
    {
        /// <summary>This instance owns the run and must send it.</summary>
        Claimed,
        /// <summary>Already sent (or being sent) by this or another instance.</summary>
        Taken,
        /// <summary>The last attempt failed recently; try again later.</summary>
        RetryLater,
        /// <summary>WN_ScheduledReportRuns has not been created yet.</summary>
        TableMissing
    }
}
