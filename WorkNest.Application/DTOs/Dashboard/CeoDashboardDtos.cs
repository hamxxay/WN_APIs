namespace WorkNest.Application.DTOs.Dashboard
{
    // CEO dashboard (api/ceo/*): read-only, all locations. Shapes follow the shared CEO contract used by the CMS and
    // the mobile app; serialized camelCase. Money in PKR, months "yyyy-MM", dates "yyyy-MM-dd".

    /// <summary>GET api/ceo/dashboard</summary>
    public class CeoDashboardDto
    {
        /// <summary>month | quarter | year</summary>
        public string Period { get; set; } = "month";
        /// <summary>e.g. "October 2026", "Q4 2026", "2026".</summary>
        public string PeriodLabel { get; set; } = "";
        /// <summary>UTC, ISO 8601 with Z.</summary>
        public string GeneratedAt { get; set; } = "";
        public List<CeoLocationDto> Locations { get; set; } = new();
        public CeoMoneyDto Money { get; set; } = new();
        public CeoOccupancyDto Occupancy { get; set; } = new();
        public CeoSalesDto Sales { get; set; } = new();
        public CeoTeamDto Team { get; set; } = new();
    }

    public class CeoLocationDto
    {
        public int LocationId { get; set; }
        public string Name { get; set; } = "";
    }

    public class CeoMoneyDto
    {
        public decimal Invoiced { get; set; }
        public decimal InvoicedPrev { get; set; }
        public decimal Collected { get; set; }
        public decimal CollectedPrev { get; set; }
        public decimal Outstanding { get; set; }
        public int OutstandingCount { get; set; }
        public decimal Overdue { get; set; }
        public int OverdueCount { get; set; }
        /// <summary>Collected / invoiced in the period, percent (one decimal).</summary>
        public decimal CollectionRate { get; set; }
        /// <summary>Last 12 months, oldest first.</summary>
        public List<CeoMoneyMonthDto> Months { get; set; } = new();
        /// <summary>Buckets not_due, 1_30, 31_60, 61_90, 90_plus.</summary>
        public List<CeoAgingBucketDto> Aging { get; set; } = new();
        /// <summary>Top 5 by invoiced amount, last 12 months.</summary>
        public List<CeoTopCustomerDto> TopCustomers { get; set; } = new();
    }

    public class CeoMoneyMonthDto
    {
        public string Month { get; set; } = "";
        public decimal Invoiced { get; set; }
        public decimal Collected { get; set; }
    }

    public class CeoAgingBucketDto
    {
        public string Bucket { get; set; } = "";
        public string Label { get; set; } = "";
        public decimal Amount { get; set; }
        public int Count { get; set; }
    }

    public class CeoTopCustomerDto
    {
        /// <summary>WN_Customers.Id; null when the invoices have no customer record (then no drill-down).</summary>
        public int? CustomerId { get; set; }
        public string Name { get; set; } = "";
        public decimal Amount { get; set; }
        public int Invoices { get; set; }
    }

    public class CeoOccupancyDto
    {
        public int TotalSpaces { get; set; }
        public int OccupiedSpaces { get; set; }
        public decimal OccupancyPct { get; set; }
        public decimal OccupancyPctPrev { get; set; }
        public int ActiveBookings { get; set; }
        public int NewBookings { get; set; }
        public int NewBookingsPrev { get; set; }
        /// <summary>Last 12 months, oldest first.</summary>
        public List<CeoOccupancyMonthDto> Months { get; set; } = new();
        public List<CeoSpaceTypeOccupancyDto> ByType { get; set; } = new();
        public List<CeoLocationOccupancyDto> ByLocation { get; set; } = new();
        /// <summary>Confirmed leases ending in the next 90 days, soonest first (the first 50).</summary>
        public List<CeoLeaseExpiringDto> LeasesExpiring { get; set; } = new();
        /// <summary>
        /// All confirmed leases ending in the next 90 days (the list holds the first 50) and their monthly rent; null
        /// before the database update QA_07 (then only the list is known, and records/leases is not available).
        /// </summary>
        public int? LeasesExpiringCount { get; set; }
        public decimal? LeasesExpiringMonthly { get; set; }
    }

    public class CeoOccupancyMonthDto
    {
        public string Month { get; set; } = "";
        public decimal OccupancyPct { get; set; }
        public int NewBookings { get; set; }
    }

    public class CeoSpaceTypeOccupancyDto
    {
        public int SpaceTypeId { get; set; }
        public string Name { get; set; } = "";
        public int Total { get; set; }
        public int Occupied { get; set; }
    }

    public class CeoLocationOccupancyDto
    {
        public int LocationId { get; set; }
        public string Name { get; set; } = "";
        public int Total { get; set; }
        public int Occupied { get; set; }
    }

    public class CeoLeaseExpiringDto
    {
        public int BookingId { get; set; }
        public string? Customer { get; set; }
        public string? Space { get; set; }
        public string? Location { get; set; }
        public string? EndOn { get; set; }
        public decimal MonthlyAmount { get; set; }
    }

    public class CeoSalesDto
    {
        /// <summary>inquiries, quotations, accepted, bookings: created in the period.</summary>
        public List<CeoFunnelStageDto> Funnel { get; set; } = new();
        /// <summary>Bookings / quotations, percent (one decimal).</summary>
        public decimal ConversionRate { get; set; }
        /// <summary>Website, WhatsApp, WalkIn, Reference, NotSet (always all five).</summary>
        public List<CeoInquiryTypeCountDto> InquiriesByType { get; set; } = new();
        /// <summary>
        /// Tour inquiries (the funnel's first stage and by type) follow the location filter by their branch
        /// (WN_Contacts.LocationId): one location = only its inquiries, all locations = every inquiry. True when a
        /// location is chosen but the figures still count every location's inquiries (database update QA_07 not run).
        /// </summary>
        public bool InquiriesAllLocations { get; set; }
        public decimal QuotationValue { get; set; }
        public decimal BookingValue { get; set; }
    }

    public class CeoFunnelStageDto
    {
        public string Stage { get; set; } = "";
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }

    public class CeoInquiryTypeCountDto
    {
        public string Type { get; set; } = "";
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }

    public class CeoTeamDto
    {
        public List<CeoTeamLocationDto> PerLocation { get; set; } = new();
        public List<CeoTeamPersonDto> People { get; set; } = new();
    }

    public class CeoTeamLocationDto
    {
        public int LocationId { get; set; }
        public string Name { get; set; } = "";
        public int Admins { get; set; }
        public int SalesExecutives { get; set; }
        public int Quotations { get; set; }
        public int Converted { get; set; }
    }

    public class CeoTeamPersonDto
    {
        public int UserId { get; set; }
        public string Name { get; set; } = "";
        /// <summary>Admin | Sales Executive</summary>
        public string Role { get; set; } = "";
        public string? Locations { get; set; }
        public int Quotations { get; set; }
        public int Converted { get; set; }
        public decimal ConvertedValue { get; set; }
    }

    /// <summary>GET api/ceo/breakdown: drill-down level 2.</summary>
    public class CeoBreakdownDto
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string ValueLabel { get; set; } = "";
        public string? Value2Label { get; set; }
        public List<CeoBreakdownRowDto> Rows { get; set; } = new();
    }

    public class CeoBreakdownRowDto
    {
        /// <summary>Location id, space type id or month (yyyy-MM); "none" = not tied to a location.</summary>
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public decimal Value { get; set; }
        public decimal? Value2 { get; set; }
        public int? Count { get; set; }
    }

    /// <summary>Query of GET api/ceo/breakdown.</summary>
    public class CeoBreakdownQuery
    {
        public string? Metric { get; set; }
        public string? Period { get; set; }
        public int? LocationId { get; set; }
        public string? Month { get; set; }
        public string? Bucket { get; set; }
        public int? SpaceTypeId { get; set; }
        public string? Type { get; set; }
        public int? UserId { get; set; }
    }

    /// <summary>GET api/ceo/records/{kind}: drill-down level 3 (newest first, at most 200 rows; Total = all).</summary>
    public class CeoRecordsDto<T>
    {
        public string Title { get; set; } = "";
        public int Total { get; set; }
        public List<T> Rows { get; set; } = new();
    }

    /// <summary>Query of GET api/ceo/records/{kind} (each list uses the parameters it knows).</summary>
    public class CeoRecordsQuery
    {
        public string? Period { get; set; }
        /// <summary>A location id, or "none" (a breakdown row not tied to a location).</summary>
        public string? LocationId { get; set; }
        public string? Month { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Bucket { get; set; }
        public int? CustomerId { get; set; }
        public string? Status { get; set; }
        public int? SpaceTypeId { get; set; }
        public bool? Occupied { get; set; }
        public string? Type { get; set; }
        public int? UserId { get; set; }
        public string? Stage { get; set; }
    }

    public class CeoInvoiceRecordDto
    {
        public int InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? Customer { get; set; }
        public string? Location { get; set; }
        public string? IssuedOn { get; set; }
        public string? DueOn { get; set; }
        public decimal Total { get; set; }
        public decimal Paid { get; set; }
        public decimal Balance { get; set; }
        public string? Status { get; set; }
    }

    public class CeoSpaceRecordDto
    {
        public int SpaceId { get; set; }
        public string? Name { get; set; }
        public string? SpaceType { get; set; }
        public string? Location { get; set; }
        public int? Capacity { get; set; }
        public bool Occupied { get; set; }
        public string? Customer { get; set; }
        public string? BookingEndOn { get; set; }
    }

    public class CeoBookingRecordDto
    {
        public int BookingId { get; set; }
        public string? Customer { get; set; }
        public string? Space { get; set; }
        public string? Location { get; set; }
        public string? StartOn { get; set; }
        public string? EndOn { get; set; }
        public string? Status { get; set; }
        public decimal TotalAmount { get; set; }
    }

    public class CeoInquiryRecordDto
    {
        public int InquiryId { get; set; }
        public string? Name { get; set; }
        public string? Phone { get; set; }
        /// <summary>Website | WhatsApp | WalkIn | Reference | NotSet</summary>
        public string? Type { get; set; }
        public string? ReferenceName { get; set; }
        /// <summary>New | In Progress | Resolved</summary>
        public string? Status { get; set; }
        /// <summary>UTC, ISO 8601 with Z.</summary>
        public string? CreatedOn { get; set; }
        /// <summary>Latest staff feedback, e.g. "Not interested: price".</summary>
        public string? Feedback { get; set; }
    }

    /// <summary>records/leases (QA_07): a confirmed lease ending in the window, soonest first.</summary>
    public class CeoLeaseRecordDto
    {
        public int BookingId { get; set; }
        public string? Customer { get; set; }
        public string? Space { get; set; }
        public string? Location { get; set; }
        /// <summary>yyyy-MM-dd</summary>
        public string? EndOn { get; set; }
        public decimal MonthlyAmount { get; set; }
        /// <summary>Days from today to the end date (negative = already ended).</summary>
        public int? DaysLeft { get; set; }
    }

    public class CeoQuotationRecordDto
    {
        public int QuotationId { get; set; }
        public string? QuotationNumber { get; set; }
        public string? Customer { get; set; }
        public string? Location { get; set; }
        public string? CreatedBy { get; set; }
        public string? Status { get; set; }
        public decimal Total { get; set; }
        /// <summary>UTC, ISO 8601 with Z.</summary>
        public string? CreatedOn { get; set; }
    }
}
