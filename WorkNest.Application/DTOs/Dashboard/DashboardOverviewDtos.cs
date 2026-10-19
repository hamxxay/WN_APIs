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
        /// <summary>Selected period: month | quarter | year (calendar, to date).</summary>
        public string Period { get; set; } = "month";
        /// <summary>Invoiced in the selected period so far / the same span of the previous period.</summary>
        public decimal InvoicedThisPeriod { get; set; }
        public decimal InvoicedPrevPeriod { get; set; }
        /// <summary>Invoiced in the selected period and now Paid.</summary>
        public decimal PaidThisPeriod { get; set; }
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

    /// <summary>
    /// The rooms behind the dashboard's Occupancy card ("X of Y spaces in use"), same rule and locations as the overview:
    /// one item per active space with a live booking (pending or confirmed, not deleted) covering this moment.
    /// </summary>
    public class DashboardOccupiedSpacesDto
    {
        /// <summary>Active spaces in the locations asked for (the card's "of Y").</summary>
        public int TotalSpaces { get; set; }
        /// <summary>Spaces in use right now (the card's "X"); always Items.Count.</summary>
        public int OccupiedSpaces { get; set; }
        public decimal OccupancyPct { get; set; }
        /// <summary>Business (Pakistan) time the list was taken at, yyyy-MM-ddTHH:mm:ss.</summary>
        public string GeneratedAt { get; set; } = string.Empty;
        /// <summary>Sorted by location, then room.</summary>
        public List<DashboardOccupiedSpaceDto> Items { get; set; } = new();
    }

    /// <summary>One space in use right now and the booking that covers it.</summary>
    public class DashboardOccupiedSpaceDto
    {
        public int SpaceId { get; set; }
        /// <summary>Space name, or its code when it has no name.</summary>
        public string Room { get; set; } = string.Empty;
        public string? SpaceCode { get; set; }
        public int? LocationId { get; set; }
        public string? Location { get; set; }
        public string SpaceType { get; set; } = string.Empty;
        /// <summary>The space's capacity.</summary>
        public int? Seats { get; set; }
        /// <summary>Seats the booking took, when it records them (shared spaces); null = not recorded.</summary>
        public int? BookedSeats { get; set; }
        /// <summary>The live booking shown for this space: a confirmed one before a pending one, then the earliest start.</summary>
        public int BookingId { get; set; }
        /// <summary>Customer company, else the customer's name (as the rest of the dashboard names customers).</summary>
        public string Customer { get; set; } = string.Empty;
        public string? CustomerCode { get; set; }
        /// <summary>Booking start / end, business time, yyyy-MM-ddTHH:mm:ss.</summary>
        public string? StartOn { get; set; }
        public string? EndOn { get; set; }
        public int StatusId { get; set; }
        /// <summary>Booking status label (Pending / Confirmed ...).</summary>
        public string Status { get; set; } = string.Empty;
        /// <summary>Live bookings on this space right now (more than 1 for e.g. morning + evening shifts).</summary>
        public int BookingCount { get; set; }
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

    // ---------- Chart drill-down: GET api/dashboard/overview-details/{kind} (DASH_02) ----------

    /// <summary>
    /// Query of GET api/dashboard/overview-details/{kind}; each kind reads the parameters it needs. period / from / to
    /// follow the dashboard (from + to, yyyy-MM-dd, both inclusive, replace the period when given).
    /// </summary>
    public class DashboardOverviewDetailsQuery
    {
        /// <summary>month (default) | quarter | year, to date: the window of top-customer.</summary>
        public string? Period { get; set; }
        /// <summary>Custom range start (yyyy-MM-dd), with <see cref="To"/>; replaces the period.</summary>
        public string? From { get; set; }
        /// <summary>Custom range end (yyyy-MM-dd, inclusive), with <see cref="From"/>.</summary>
        public string? To { get; set; }
        /// <summary>yyyy-MM: revenue-month, new-bookings, occupied-month, leases-ending.</summary>
        public string? Month { get; set; }
        /// <summary>aging-bucket: not_due | 1_30 | 31_60 | 61_90 | 90_plus.</summary>
        public string? Bucket { get; set; }
        /// <summary>space-type: the space type as the chart names it.</summary>
        public string? SpaceType { get; set; }
        /// <summary>top-customer: the customer as the chart names it.</summary>
        public string? Customer { get; set; }
    }

    /// <summary>
    /// The records behind one bar / slice / point of an overview chart, on exactly the overview's rules, so the totals
    /// here are the chart's figure. Rows holds the first 200 (spaces: 500); the totals count every match.
    /// </summary>
    public class DashboardOverviewDetailsDto
    {
        /// <summary>The kind asked for (revenue-month, aging-bucket, top-customer, new-bookings, leases-ending, space-type, occupied-month).</summary>
        public string Kind { get; set; } = string.Empty;
        /// <summary>Row shape: invoices (<see cref="DashboardDetailInvoiceDto"/>), bookings (<see cref="DashboardDetailBookingDto"/>) or spaces (<see cref="DashboardDetailSpaceDto"/>).</summary>
        public string Records { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        /// <summary>Every matching record (Rows may hold fewer).</summary>
        public int Total { get; set; }
        /// <summary>Invoices: GrandTotal of all (invoiced); bookings: TotalAmount of all.</summary>
        public decimal Amount { get; set; }
        /// <summary>Invoices: GrandTotal of the paid ones (the chart's Paid).</summary>
        public decimal PaidAmount { get; set; }
        /// <summary>Invoices: still owed (GrandTotal - WHT - paid) of the open ones (the aging amount).</summary>
        public decimal Balance { get; set; }
        /// <summary>Spaces: how many of them are in use at the moment the list is for.</summary>
        public int Occupied { get; set; }
        /// <summary>Spaces: active spaces in the locations (the occupancy chart's "of").</summary>
        public int TotalSpaces { get; set; }
        /// <summary>The window the list covers, yyyy-MM-dd (To null = no end, as the chart).</summary>
        public string? From { get; set; }
        public string? To { get; set; }
        /// <summary>Business (Pakistan) time the list was taken at, yyyy-MM-ddTHH:mm:ss.</summary>
        public string GeneratedAt { get; set; } = string.Empty;
        public List<object> Rows { get; set; } = new();
    }

    public class DashboardDetailInvoiceDto
    {
        public int InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public int? BookingId { get; set; }
        public string Customer { get; set; } = string.Empty;
        public string? Space { get; set; }
        public string? Location { get; set; }
        /// <summary>yyyy-MM-dd</summary>
        public string? IssuedOn { get; set; }
        public string? DueOn { get; set; }
        /// <summary>GrandTotal.</summary>
        public decimal Total { get; set; }
        /// <summary>PaidTotal.</summary>
        public decimal Paid { get; set; }
        /// <summary>Withholding tax the customer keeps (not owed).</summary>
        public decimal Wht { get; set; }
        /// <summary>Still owed: GrandTotal - WHT - paid for an open invoice, else 0.</summary>
        public decimal Balance { get; set; }
        public bool IsOpen { get; set; }
        public bool IsPaid { get; set; }
        public int DaysOverdue { get; set; }
        /// <summary>Unpaid / Paid / Partial / Overdue / ...</summary>
        public string Status { get; set; } = string.Empty;
    }

    public class DashboardDetailBookingDto
    {
        public int BookingId { get; set; }
        public string Customer { get; set; } = string.Empty;
        public string? Space { get; set; }
        public string? SpaceType { get; set; }
        public int? LocationId { get; set; }
        public string? Location { get; set; }
        /// <summary>yyyy-MM-ddTHH:mm:ss (business time).</summary>
        public string? CreatedOn { get; set; }
        public string? StartOn { get; set; }
        public string? EndOn { get; set; }
        public int StatusId { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        /// <summary>Seats the booking took, when it records them; null = not recorded.</summary>
        public int? Seats { get; set; }
    }

    public class DashboardDetailSpaceDto
    {
        public int SpaceId { get; set; }
        /// <summary>Space name, or its code when it has no name.</summary>
        public string Name { get; set; } = string.Empty;
        public string? SpaceCode { get; set; }
        public string SpaceType { get; set; } = string.Empty;
        public int? LocationId { get; set; }
        public string? Location { get; set; }
        /// <summary>The space's capacity.</summary>
        public int? Capacity { get; set; }
        /// <summary>In use at the moment the list is for.</summary>
        public bool Occupied { get; set; }
        /// <summary>The live booking shown (confirmed before pending, then the earliest start); null when vacant.</summary>
        public int? BookingId { get; set; }
        public string? Customer { get; set; }
        public string? BookingStartOn { get; set; }
        public string? BookingEndOn { get; set; }
        public string? Status { get; set; }
        /// <summary>Live bookings on the space at that moment (more than 1 for e.g. morning + evening shifts).</summary>
        public int BookingCount { get; set; }
    }
}
