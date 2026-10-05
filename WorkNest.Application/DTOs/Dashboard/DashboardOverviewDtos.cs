namespace WorkNest.Application.DTOs.Dashboard
{
    /// <summary>Admin dashboard: headline numbers with last-month comparison, 6-month trend and "needs attention" lists.</summary>
    public class DashboardOverviewDto
    {
        public int TotalSpaces { get; set; }
        public int OccupiedSpaces { get; set; }
        public int OccupiedSpacesLastMonth { get; set; }
        public decimal OccupancyPct { get; set; }
        public decimal OccupancyPctLastMonth { get; set; }
        public int ActiveBookings { get; set; }
        public int ActiveBookingsLastMonth { get; set; }
        public int PendingConfirmations { get; set; }
        public decimal Outstanding { get; set; }
        public int OutstandingCount { get; set; }
        public decimal OverdueAmount { get; set; }
        public int OverdueCount { get; set; }
        public decimal InvoicedThisMonth { get; set; }
        public decimal InvoicedLastMonth { get; set; }
        public int LeasesEndingSoon { get; set; }
        public int EndingSoonDays { get; set; }
        public List<DashboardMonthDto> Months { get; set; } = new();
        public List<DashboardAttentionItemDto> OverdueInvoices { get; set; } = new();
        public List<DashboardAttentionItemDto> EndingLeases { get; set; } = new();
        public List<DashboardAttentionItemDto> PendingBookings { get; set; } = new();
        public List<DashboardBucketDto> Aging { get; set; } = new();
        public List<DashboardSpaceTypeDto> SpaceTypes { get; set; } = new();
        public List<DashboardBucketDto> TopCustomers { get; set; } = new();
        public List<DashboardBucketDto> LeaseExpiries { get; set; } = new();
        public string GeneratedAt { get; set; } = string.Empty;
    }

    public class DashboardMonthDto
    {
        /// <summary>yyyy-MM</summary>
        public string Month { get; set; } = string.Empty;
        public decimal Invoiced { get; set; }
        /// <summary>Invoiced that month and now Paid.</summary>
        public decimal Paid { get; set; }
        public decimal OccupancyPct { get; set; }
        public int NewBookings { get; set; }
    }

    /// <summary>A labelled amount + count (aging bucket, customer, expiry month).</summary>
    public class DashboardBucketDto
    {
        public string Label { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public int Count { get; set; }
    }

    public class DashboardSpaceTypeDto
    {
        public string SpaceType { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Occupied { get; set; }
    }

    public class DashboardAttentionItemDto
    {
        public int? BookingId { get; set; }
        public string? Reference { get; set; }
        public string? Customer { get; set; }
        public string? Space { get; set; }
        /// <summary>Due date / lease end / booking start (yyyy-MM-dd).</summary>
        public string? Date { get; set; }
        public decimal? Amount { get; set; }
    }
}
