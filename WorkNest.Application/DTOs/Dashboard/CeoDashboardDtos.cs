namespace WorkNest.Application.DTOs.Dashboard
{
    // CEO dashboard (api/ceo/*): read-only, all locations. Shapes follow the shared CEO contract used by the CMS and
    // the mobile app; serialized camelCase. Money in PKR, months "yyyy-MM", dates "yyyy-MM-dd".

    /// <summary>GET api/ceo/dashboard</summary>
    public class CeoDashboardDto
    {
        /// <summary>month | quarter | year | custom</summary>
        public string Period { get; set; } = "month";
        /// <summary>e.g. "October 2026", "Q4 2026", "2026", "1 Oct - 15 Oct 2026".</summary>
        public string PeriodLabel { get; set; } = "";
        /// <summary>UTC, ISO 8601 with Z.</summary>
        public string GeneratedAt { get; set; } = "";
        public List<CeoLocationDto> Locations { get; set; } = new();
        public CeoMoneyDto Money { get; set; } = new();
        public CeoOccupancyDto Occupancy { get; set; } = new();
        public CeoSalesDto Sales { get; set; } = new();
        public CeoTeamDto Team { get; set; } = new();
        /// <summary>Custom period (CS_01): the first and last day of the range, yyyy-MM-dd; null for month / quarter / year.</summary>
        public string? From { get; set; }
        public string? To { get; set; }
        /// <summary>Custom period: the previous range the *Prev figures cover (as many days, just before From); else null.</summary>
        public string? PreviousFrom { get; set; }
        public string? PreviousTo { get; set; }
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
        /// <summary>Quotations created in the period that were booked / quotations created in it, percent (one decimal); never above 100.</summary>
        public decimal ConversionRate { get; set; }
        /// <summary>Quotations created in the period that were booked (converted).</summary>
        public int QuotationsBooked { get; set; }
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
        /// <summary>Metric revenue (CS_01): invoices issued from / to (yyyy-MM-dd, both days included) instead of a month.</summary>
        public string? From { get; set; }
        public string? To { get; set; }
        /// <summary>period = custom (CS_01): the dashboard's range, yyyy-MM-dd (from / to narrow a list on their own).</summary>
        public string? PeriodFrom { get; set; }
        public string? PeriodTo { get; set; }
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
        /// <summary>period = custom (CS_01): the dashboard's range, yyyy-MM-dd (from / to narrow a list on their own).</summary>
        public string? PeriodFrom { get; set; }
        public string? PeriodTo { get; set; }
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

    /// <summary>
    /// GET api/ceo/rooms: rooms sold monthly (private offices, shared spaces), their occupancy and the price each was
    /// sold at against the list price per seat (24/7) or the shift price (Morning / Evening: list less the location's
    /// shift discount cap). Before the database update CEO_02: Available = false with Message, no summary, no rooms.
    /// </summary>
    public class CeoRoomReportDto
    {
        public bool Available { get; set; } = true;
        /// <summary>Set when Available is false: what to do (run CEO_02_room_report).</summary>
        public string? Message { get; set; }
        /// <summary>month | quarter | year | custom</summary>
        public string Period { get; set; } = "month";
        /// <summary>e.g. "October 2026", "Q4 2026", "2026", "1 Oct - 15 Oct 2026".</summary>
        public string PeriodLabel { get; set; } = "";
        /// <summary>UTC, ISO 8601 with Z.</summary>
        public string GeneratedAt { get; set; } = "";
        /// <summary>Null when Available is false.</summary>
        public CeoRoomSummaryDto? Summary { get; set; }
        /// <summary>By location, then room code / name (numbers in order: 9 before 10).</summary>
        public List<CeoRoomDto> Rooms { get; set; } = new();
        /// <summary>Custom period (CS_01): the first and last day of the range, yyyy-MM-dd; null for month / quarter / year.</summary>
        public string? From { get; set; }
        public string? To { get; set; }
    }

    public class CeoRoomSummaryDto
    {
        public int Rooms { get; set; }
        /// <summary>Rooms with a confirmed booking right now.</summary>
        public int OccupiedNow { get; set; }
        public int VacantNow { get; set; }
        /// <summary>Occupied now / rooms, percent (one decimal).</summary>
        public decimal OccupancyNowPct { get; set; }
        /// <summary>Room-days with a confirmed booking / room-days of the period so far, percent (one decimal).</summary>
        public decimal PeriodOccupancyPct { get; set; }
        /// <summary>Confirmed bookings on the rooms running in the period (Under + At + Over + Unpriced).</summary>
        public int Sales { get; set; }
        public int Under { get; set; }
        public int At { get; set; }
        public int Over { get; set; }
        /// <summary>Sales with no list price known (no pricing row): not compared, not in Under / At / Over.</summary>
        public int Unpriced { get; set; }
        /// <summary>Sum of the expected monthly (list or shift price x seats) of the priced sales.</summary>
        public decimal ListMonthly { get; set; }
        /// <summary>Sum of the monthly sold (after the booking discount) of all sales.</summary>
        public decimal SoldMonthly { get; set; }
        /// <summary>Sum of the monthly variance of the priced sales (negative = sold below the price).</summary>
        public decimal VarianceMonthly { get; set; }
        /// <summary>Monthly gap of the under-priced sales (sum of their variance, zero or negative).</summary>
        public decimal UnderMonthly { get; set; }
        /// <summary>Monthly premium of the over-priced sales (sum of their variance, zero or positive).</summary>
        public decimal OverMonthly { get; set; }
        /// <summary>Always three: 24/7, Morning, Evening.</summary>
        public List<CeoRoomShiftSummaryDto> ByShift { get; set; } = new();
    }

    public class CeoRoomShiftSummaryDto
    {
        /// <summary>24/7 | Morning | Evening</summary>
        public string Shift { get; set; } = "";
        public int Sales { get; set; }
        public int Under { get; set; }
        public int At { get; set; }
        public int Over { get; set; }
        public int Unpriced { get; set; }
        public decimal ExpectedMonthly { get; set; }
        public decimal SoldMonthly { get; set; }
        public decimal VarianceMonthly { get; set; }
    }

    public class CeoRoomDto
    {
        public int SpaceId { get; set; }
        public string? Name { get; set; }
        public string? Code { get; set; }
        public int? LocationId { get; set; }
        public string? Location { get; set; }
        public string? SpaceType { get; set; }
        /// <summary>PrivateOffice | SharedSpace</summary>
        public string? CategoryCode { get; set; }
        /// <summary>Seats of the room (WN_Spaces.Capacity).</summary>
        public int Seats { get; set; }
        /// <summary>Today's monthly list price per seat; null when the room has no active monthly price.</summary>
        public decimal? ListSeatPrice { get; set; }
        /// <summary>List price per seat x seats; null when there is no list price.</summary>
        public decimal? ListRoomPrice { get; set; }
        public bool OccupiedNow { get; set; }
        /// <summary>Days of the period so far with a confirmed booking, percent (one decimal).</summary>
        public decimal PeriodOccupancyPct { get; set; }
        /// <summary>Confirmed bookings running in the period, by start; empty = vacant all period.</summary>
        public List<CeoRoomSaleDto> Sales { get; set; } = new();
    }

    public class CeoRoomSaleDto
    {
        public int BookingId { get; set; }
        public string? Customer { get; set; }
        /// <summary>24/7 | Morning | Evening</summary>
        public string Shift { get; set; } = "24/7";
        public int SeatsSold { get; set; }
        /// <summary>yyyy-MM-dd</summary>
        public string? StartOn { get; set; }
        /// <summary>yyyy-MM-dd</summary>
        public string? EndOn { get; set; }
        /// <summary>List price per seat when sold (the booking's pricing row, else today's); null when unknown.</summary>
        public decimal? ListSeatPriceAtSale { get; set; }
        /// <summary>The location's discount cap for the shift, percent (applied to Morning / Evening only).</summary>
        public decimal ShiftDiscountCapPct { get; set; }
        /// <summary>24/7: the list price per seat; Morning / Evening: list x (1 - cap / 100). Null when unpriced.</summary>
        public decimal? ExpectedSeatPrice { get; set; }
        /// <summary>Expected per seat x seats sold. Null when unpriced.</summary>
        public decimal? ExpectedMonthly { get; set; }
        /// <summary>Monthly rent of the room after the booking discount.</summary>
        public decimal SoldMonthly { get; set; }
        /// <summary>Sold monthly / seats sold.</summary>
        public decimal SoldSeatPrice { get; set; }
        /// <summary>(Sold per seat - expected per seat) x seats sold. Null when unpriced.</summary>
        public decimal? VarianceMonthly { get; set; }
        /// <summary>Sold per seat / expected per seat - 1, percent (two decimals). Null when unpriced.</summary>
        public decimal? VariancePct { get; set; }
        /// <summary>under | at | over (more than 0.5% below / above the expected price); null when unpriced.</summary>
        public string? Pricing { get; set; }
    }
}
