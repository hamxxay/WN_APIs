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
        /// <summary>
        /// Invoiced / collected in the period by billing period (CP_01: the month the period starts, not the issue date).
        /// CP_02: invoiced = GrandTotal less the refundable security deposit (<see cref="SecurityDeposits"/> apart);
        /// collected = paid so far on those invoices (partial payments count, a Paid invoice in full, an invoice with only
        /// its WHT left counts as settled), less the deposit's share. A calendar period counts whole billing months up to
        /// this one, against the same months one period earlier (<see cref="InvoicedPrevFrom"/> / <see cref="InvoicedPrevTo"/>).
        /// </summary>
        public decimal Invoiced { get; set; }
        public decimal InvoicedPrev { get; set; }
        public decimal Collected { get; set; }
        public decimal CollectedPrev { get; set; }
        /// <summary>
        /// CP_02: security deposits billed on the period's invoices (left out of Invoiced, the 12 months and the top
        /// customers), the previous period's, and how many invoices carry one; null before the database update CP_02.
        /// </summary>
        public decimal? SecurityDeposits { get; set; }
        public decimal? SecurityDepositsPrev { get; set; }
        public int? SecurityDepositsCount { get; set; }
        /// <summary>CP_02: the billing period starts InvoicedPrev / CollectedPrev cover (yyyy-MM-dd, both days included); null before CP_02.</summary>
        public string? InvoicedPrevFrom { get; set; }
        public string? InvoicedPrevTo { get; set; }
        /// <summary>
        /// RC_05: the WHT not paid on the period's / previous period's billing (each month's WHT, at most what is left of the
        /// month): the customer withholds it and never pays it, so Receivable = Invoiced - Collected - WhtOpen (the CMS works
        /// it out). Null before the database update RC_05.
        /// </summary>
        public decimal? WhtOpen { get; set; }
        public decimal? WhtOpenPrev { get; set; }
        /// <summary>Open invoices owed now (CP_01: without the ones billed in advance).</summary>
        public decimal Outstanding { get; set; }
        public int OutstandingCount { get; set; }
        public decimal Overdue { get; set; }
        public int OverdueCount { get; set; }
        /// <summary>
        /// CP_01: unpaid invoices for a billing period that has not started yet and not past due (GrandTotal - WHT -
        /// paid, as Outstanding, and not in it); null before the database update CP_01 has run.
        /// </summary>
        public decimal? BilledInAdvance { get; set; }
        public int? BilledInAdvanceCount { get; set; }
        /// <summary>
        /// CP_02: money actually received in the period, to date (active fee receipts by receipt date + paid payments by
        /// paid date, whatever invoice; a payment for an invoice with an active fee receipt counts once), same location
        /// rule; previous = the same days one period earlier. Null before CP_02 (dbo.WN_Ceo_Received).
        /// </summary>
        public decimal? Received { get; set; }
        public decimal? ReceivedPrev { get; set; }
        public int? ReceivedCount { get; set; }
        public int? ReceivedCountPrev { get; set; }
        /// <summary>CP_02: the part of Received from fee receipts / from payments.</summary>
        public decimal? ReceivedFromReceipts { get; set; }
        public decimal? ReceivedFromPayments { get; set; }
        /// <summary>CP_02: money received by month, the same 12 months as Months (oldest first); null before CP_02.</summary>
        public List<CeoReceivedMonthDto>? ReceivedSeries { get; set; }
        /// <summary>Collected / invoiced in the period, percent (one decimal): the share of the period's billing paid so far.</summary>
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
        /// <summary>RC_05: the WHT not paid on the month's billing (as <see cref="CeoMoneyDto.WhtOpen"/>); null before RC_05.</summary>
        public decimal? WhtOpen { get; set; }
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

    /// <summary>
    /// Occupancy (CP_02, 20 Oct 2026): ROOMS (space category PrivateOffice or SharedSpace; meeting rooms etc. left out)
    /// with a CONFIRMED booking (2, 33) right now, shown per category (<see cref="Categories"/>), never as one mixed figure.
    /// TotalSpaces / OccupiedSpaces / OccupancyPct(Prev) are both categories together (kept for older clients); before
    /// CP_02 they are every active space with a confirmed or pending booking, and Categories is empty.
    /// </summary>
    public class CeoOccupancyDto
    {
        public int TotalSpaces { get; set; }
        public int OccupiedSpaces { get; set; }
        public decimal OccupancyPct { get; set; }
        public decimal OccupancyPctPrev { get; set; }
        public int ActiveBookings { get; set; }
        public int NewBookings { get; set; }
        public int NewBookingsPrev { get; set; }
        /// <summary>CP_02: seats of the rooms / seats sold of the confirmed bookings running now (both categories); null before CP_02.</summary>
        public int? TotalSeats { get; set; }
        public int? SeatsSold { get; set; }
        public decimal? SeatOccupancyPct { get; set; }
        /// <summary>CP_02: Private Office, then Shared Space (always both, zeros when none); empty before CP_02.</summary>
        public List<CeoOccupancyCategoryDto> Categories { get; set; } = new();
        /// <summary>Last 12 months, oldest first.</summary>
        public List<CeoOccupancyMonthDto> Months { get; set; } = new();
        /// <summary>By space type: only before CP_02 (since CP_02 the procedure gives <see cref="Categories"/> instead; empty).</summary>
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
        /// <summary>Occupancy at the month end (this month: now); CP_02: of the rooms that existed then, both categories.</summary>
        public decimal OccupancyPct { get; set; }
        public int NewBookings { get; set; }
        /// <summary>CP_02: per category at the month end, Private Office then Shared Space; empty before CP_02.</summary>
        public List<CeoOccupancyCategoryMonthDto> Categories { get; set; } = new();
    }

    /// <summary>One room category's occupancy now (CP_02).</summary>
    public class CeoOccupancyCategoryDto
    {
        /// <summary>WN_SpaceCategories.Code: PrivateOffice | SharedSpace.</summary>
        public string CategoryCode { get; set; } = "";
        /// <summary>WN_SpaceCategories.Label (e.g. "Private Office").</summary>
        public string Label { get; set; } = "";
        public int Rooms { get; set; }
        /// <summary>Rooms with a confirmed booking right now.</summary>
        public int Occupied { get; set; }
        public decimal OccupancyPct { get; set; }
        /// <summary>One period ago (same moment), of the rooms that existed then.</summary>
        public int RoomsPrev { get; set; }
        public int OccupiedPrev { get; set; }
        public decimal OccupancyPctPrev { get; set; }
        public int Seats { get; set; }
        public int SeatsSold { get; set; }
        public decimal SeatOccupancyPct { get; set; }
    }

    /// <summary>One room category at a month end (CP_02): rooms that existed then and how many had a confirmed booking.</summary>
    public class CeoOccupancyCategoryMonthDto
    {
        public string CategoryCode { get; set; } = "";
        public string Label { get; set; } = "";
        public int Rooms { get; set; }
        public int Occupied { get; set; }
        public decimal OccupancyPct { get; set; }
    }

    /// <summary>One room category at one location, now (CP_02).</summary>
    public class CeoLocationCategoryOccupancyDto
    {
        public string CategoryCode { get; set; } = "";
        public string Label { get; set; } = "";
        public int Rooms { get; set; }
        public int Occupied { get; set; }
        public decimal OccupancyPct { get; set; }
        public int Seats { get; set; }
        public int SeatsSold { get; set; }
        public decimal SeatOccupancyPct { get; set; }
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
        /// <summary>0 = "No location" (CP_02: rooms not tied to a shown location; records with locationId=none).</summary>
        public int LocationId { get; set; }
        public string Name { get; set; } = "";
        public int Total { get; set; }
        public int Occupied { get; set; }
        /// <summary>CP_02: seats / seats sold now (both categories); null before CP_02.</summary>
        public int? Seats { get; set; }
        public int? SeatsSold { get; set; }
        /// <summary>CP_02: Private Office, then Shared Space at this location; empty before CP_02.</summary>
        public List<CeoLocationCategoryOccupancyDto> Categories { get; set; } = new();
    }

    public class CeoLeaseExpiringDto
    {
        public int BookingId { get; set; }
        public string? Customer { get; set; }
        public string? Space { get; set; }
        public string? Location { get; set; }
        public string? EndOn { get; set; }
        /// <summary>CP_02: the monthly rent after the % and fixed PKR discounts (before CP_02: the list rent).</summary>
        public decimal MonthlyAmount { get; set; }
    }

    public class CeoSalesDto
    {
        /// <summary>inquiries, quotations, accepted, bookings: created in the period.</summary>
        public List<CeoFunnelStageDto> Funnel { get; set; } = new();
        /// <summary>
        /// Quotations created in the period that were booked / quotations created in it, percent (one decimal); never above
        /// 100. CP_02: booked = Converted and the booking is live (not deleted, not rejected / cancelled / no show).
        /// </summary>
        public decimal ConversionRate { get; set; }
        /// <summary>Quotations created in the period that were booked (converted; CP_02: with a live booking).</summary>
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
        /// <summary>
        /// Value of the bookings made in the period (the Bookings stage). CP_02: their contract value (the rent of the whole
        /// lease after the discount, as the booking summary shows it); before CP_02: the first invoice (challan) of each
        /// booking, else its total. <see cref="BookingValueBasis"/> says which.
        /// </summary>
        public decimal BookingValue { get; set; }
        /// <summary>contract (CP_02) | firstInvoice (before CP_02).</summary>
        public string BookingValueBasis { get; set; } = "firstInvoice";
        /// <summary>
        /// CP_02: of the period's bookings (the Bookings stage), those made from a quotation (converted); the rest were
        /// booked directly. Null before CP_02.
        /// </summary>
        public int? BookingsFromQuotations { get; set; }
        /// <summary>
        /// CP_02: one location chosen (inquiries by branch): the tour inquiries of the period that have no branch, so are
        /// counted at no location (not in the figures). Null with all locations or before CP_02.
        /// </summary>
        public int? InquiriesNoBranch { get; set; }
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

    /// <summary>
    /// Team section: the sales executives only (WN_Users.RoleId 16, active; 19 Oct 2026: no admins or other roles), each
    /// with their revenue. With a location chosen: the sales executives of that location (WN_Users.LocationId or
    /// WN_UserLocations) and only their quotations on that location's spaces.
    /// </summary>
    public class CeoTeamDto
    {
        public List<CeoTeamLocationDto> PerLocation { get; set; } = new();
        /// <summary>Sales executives, highest revenue first (then converted, quotations, name).</summary>
        public List<CeoTeamPersonDto> People { get; set; } = new();
        /// <summary>How many sales executives <see cref="People"/> lists.</summary>
        public int SalesExecutives { get; set; }
        /// <summary>Their revenue together (the sum of <see cref="CeoTeamPersonDto.Revenue"/>), PKR.</summary>
        public decimal Revenue { get; set; }
        /// <summary>
        /// What Revenue is: bookings (CP_02: the contract value of the new bookings each person sold in the period) or
        /// quotations (before CP_02: the value of their quotations created in the period that were converted).
        /// </summary>
        public string RevenueBasis { get; set; } = "quotations";
    }

    /// <summary>
    /// One location: its sales executives and the quotations created in the period on its spaces, how many converted
    /// (with a live booking). CP_02: only the quotations sales executives created (<see cref="SalesExecutivesOnly"/>);
    /// before it, by anyone (as the sales funnel).
    /// </summary>
    public class CeoTeamLocationDto
    {
        public int LocationId { get; set; }
        public string Name { get; set; } = "";
        public int SalesExecutives { get; set; }
        public int Quotations { get; set; }
        public int Converted { get; set; }
        /// <summary>CP_02: Quotations / Converted count only the sales executives' quotations (false before CP_02: anyone's).</summary>
        public bool SalesExecutivesOnly { get; set; }
    }

    public class CeoTeamPersonDto
    {
        public int UserId { get; set; }
        /// <summary>WN_Users.Name.</summary>
        public string Name { get; set; } = "";
        /// <summary>WN_Roles.DisplayName of the sales executive role ("" when the role list cannot be read).</summary>
        public string Role { get; set; } = "";
        public string? Locations { get; set; }
        public int Quotations { get; set; }
        public int Converted { get; set; }
        /// <summary>
        /// Revenue, PKR. CP_02: the contract value of the new bookings (created in the period, live) this person sold:
        /// seller = who made the booking's quotation, else who created the booking (as the Discounts section).
        /// Before CP_02: the total of the quotations this person created in the period that were converted.
        /// </summary>
        public decimal Revenue { get; set; }
        /// <summary>Same as <see cref="Revenue"/> (the name older app builds read).</summary>
        public decimal ConvertedValue { get; set; }
        /// <summary>CP_02: how many new bookings make up <see cref="Revenue"/>; null before CP_02.</summary>
        public int? Bookings { get; set; }
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
        /// <summary>
        /// RC_05, metric revenue: the WHT not paid on the row's billing (the customer withholds it), so the row's receivable
        /// = Value - Value2 - Wht; null for other metrics and before RC_05.
        /// </summary>
        public decimal? Wht { get; set; }
    }

    /// <summary>Query of GET api/ceo/breakdown.</summary>
    public class CeoBreakdownQuery
    {
        public string? Metric { get; set; }
        public string? Period { get; set; }
        public int? LocationId { get; set; }
        public string? Month { get; set; }
        /// <summary>Metric revenue (CS_01): invoices whose billing period starts from / to (yyyy-MM-dd, both days included; CP_01) instead of a month.</summary>
        public string? From { get; set; }
        public string? To { get; set; }
        /// <summary>period = custom (CS_01): the dashboard's range, yyyy-MM-dd (from / to narrow a list on their own).</summary>
        public string? PeriodFrom { get; set; }
        public string? PeriodTo { get; set; }
        public string? Bucket { get; set; }
        public int? SpaceTypeId { get; set; }
        /// <summary>CP_02, metric occupancy-category: PrivateOffice | SharedSpace.</summary>
        public string? Category { get; set; }
        public string? Type { get; set; }
        public int? UserId { get; set; }
    }

    /// <summary>GET api/ceo/records/{kind}: drill-down level 3 (newest first, at most 200 rows; Total = all).</summary>
    public class CeoRecordsDto<T>
    {
        public string Title { get; set; } = "";
        public int Total { get; set; }
        public List<T> Rows { get; set; } = new();
        /// <summary>CP_02, invoices only: the totals of every matching invoice; null otherwise.</summary>
        public CeoInvoiceTotalsDto? InvoiceTotals { get; set; }
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
        /// <summary>CP_02, spaces: only rooms of this category (PrivateOffice | SharedSpace).</summary>
        public string? Category { get; set; }
        /// <summary>CP_02, spaces: true = only rooms (Private Office and Shared Space), as the dashboard's occupancy.</summary>
        public bool? Rooms { get; set; }
        public bool? Occupied { get; set; }
        public string? Type { get; set; }
        public int? UserId { get; set; }
        public string? Stage { get; set; }
        /// <summary>
        /// CP_02, bookings: true = the sales list (the period's new bookings at their contract value, with the seller),
        /// as the Sales funnel's Booked value; implied by <see cref="SellerId"/>.
        /// </summary>
        public bool? Sales { get; set; }
        /// <summary>CP_02, bookings: only the new bookings this user sold (a sales executive's revenue).</summary>
        public int? SellerId { get; set; }
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
        /// <summary>Owed now: GrandTotal - WHT - paid (CP_02; WHT is withheld by the customer), 0 for a paid invoice.</summary>
        public decimal Balance { get; set; }
        public string? Status { get; set; }
        /// <summary>CP_01: the billing period, yyyy-MM-dd (end null when unknown); null before the database update CP_01.</summary>
        public string? PeriodStart { get; set; }
        public string? PeriodEnd { get; set; }
        /// <summary>
        /// CP_02 (null before it): WHT withheld; the security deposit inside Total; billed = Total less the deposit (what
        /// Invoiced counts); collected = paid so far less the deposit's share (what Collected counts).
        /// </summary>
        public decimal? Wht { get; set; }
        public decimal? Deposit { get; set; }
        public decimal? Billed { get; set; }
        public decimal? Collected { get; set; }
        /// <summary>RC_05 (null before it): the WHT not paid (with a period: of its months; withheld by the customer).</summary>
        public decimal? WhtOpen { get; set; }
        /// <summary>
        /// RC_05: billed - collected - WHT not paid of the period's months (what the customer still has to pay for them);
        /// sent only for a list with a period (month, from / to, a customer's 12 months), else null.
        /// </summary>
        public decimal? Receivable { get; set; }
    }

    /// <summary>CP_02: records/invoices totals of every matching invoice (not only the rows sent); null before CP_02.</summary>
    public class CeoInvoiceTotalsDto
    {
        /// <summary>Billed without security deposits (as Invoiced).</summary>
        public decimal Billed { get; set; }
        /// <summary>Paid so far without the deposits' share (as Collected).</summary>
        public decimal Collected { get; set; }
        /// <summary>Owed now (GrandTotal - WHT - paid of the invoices with something left to pay).</summary>
        public decimal Balance { get; set; }
        /// <summary>
        /// RC_05: billed - collected - WHT not paid of the list's period (what the Receivable card counts); null without a
        /// period or before RC_05.
        /// </summary>
        public decimal? Receivable { get; set; }
        public decimal? WhtOpen { get; set; }
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
        /// <summary>CP_02: the room category (label / code; null for a space without one or before CP_02).</summary>
        public string? Category { get; set; }
        public string? CategoryCode { get; set; }
        /// <summary>CP_02: seats of the confirmed bookings running now (at most the room's seats); null before CP_02.</summary>
        public int? SeatsSold { get; set; }
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
        /// <summary>
        /// CP_02, the sales list (records/bookings?sales=true): who sold the booking (their quotation, else who created the
        /// booking) and whether it came from a quotation; null on the other booking lists.
        /// </summary>
        public int? SellerId { get; set; }
        public string? Seller { get; set; }
        public bool? FromQuotation { get; set; }
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
        /// <summary>
        /// Occupancy per room category, from the same rooms as the totals above (the categories' Rooms / OccupiedNow /
        /// VacantNow add up to them): always Private Office then Shared Space (zeros when the location has none), then
        /// any other category the rooms carry.
        /// </summary>
        public List<CeoRoomCategorySummaryDto> ByCategory { get; set; } = new();
    }

    public class CeoRoomCategorySummaryDto
    {
        /// <summary>WN_SpaceCategories.Code: PrivateOffice | SharedSpace.</summary>
        public string CategoryCode { get; set; } = "";
        /// <summary>WN_SpaceCategories.Label (e.g. "Private Office"); worked out from the code only when the database has none.</summary>
        public string Label { get; set; } = "";
        public int Rooms { get; set; }
        /// <summary>Rooms of the category with a confirmed booking right now.</summary>
        public int OccupiedNow { get; set; }
        public int VacantNow { get; set; }
        /// <summary>Occupied now / rooms, percent (one decimal).</summary>
        public decimal OccupancyNowPct { get; set; }
        /// <summary>Days occupied: room-days with a confirmed booking / room-days of the period so far, percent (one decimal).</summary>
        public decimal PeriodOccupancyPct { get; set; }
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
        /// <summary>Days of the period so far with a confirmed booking (PeriodOccupancyPct = OccupiedDays / PeriodDays).</summary>
        public int OccupiedDays { get; set; }
        /// <summary>Days of the period so far (the same for every room).</summary>
        public int PeriodDays { get; set; }
        /// <summary>Confirmed bookings running in the period, by start; empty = vacant all period.</summary>
        public List<CeoRoomSaleDto> Sales { get; set; } = new();
        /// <summary>CP_02: yyyy-MM-dd start of the next confirmed booking that starts after the period (and after now); null when none.</summary>
        public string? UpcomingStartOn { get; set; }
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
        /// <summary>List price per seat: the room's own price (WN_Spaces.Price, RM_01), else the booking's pricing row, else today's; null when unknown.</summary>
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
        /// <summary>under | at | over (more than 0.5% below / above the expected price; a fixed PKR discount below it is under); null when unpriced.</summary>
        public string? Pricing { get; set; }
        /// <summary>CP_02: the booking's fixed PKR discount a month, already taken off SoldMonthly (0 when none).</summary>
        public decimal FixedMonthly { get; set; }
        /// <summary>CP_02: the booking's discount is a fixed PKR amount (DiscountType Amount / Fixed).</summary>
        public bool FixedDiscount { get; set; }
    }

    /// <summary>CP_02: money received in one month (by the day it came in).</summary>
    public class CeoReceivedMonthDto
    {
        public string Month { get; set; } = "";
        public decimal Received { get; set; }
        public int Count { get; set; }
    }

    /// <summary>CP_02: records/receipts: the list, plus the sum of every matching row (not only the first 200).</summary>
    public class CeoReceiptRecordsDto : CeoRecordsDto<CeoReceiptRecordDto>
    {
        public decimal Amount { get; set; }
    }

    /// <summary>CP_02: one fee receipt or paid payment (dbo.WN_Ceo_Records_Receipts).</summary>
    public class CeoReceiptRecordDto
    {
        /// <summary>receipt (WN_FeeReceipts) | payment (WN_Payments)</summary>
        public string Kind { get; set; } = "";
        /// <summary>WN_FeeReceipts.Id or WN_Payments.Id.</summary>
        public int RefId { get; set; }
        /// <summary>Receipt code, or the payment's transaction / gateway reference ("Payment #id" when none).</summary>
        public string? Reference { get; set; }
        /// <summary>yyyy-MM-dd, the Pakistan day the money came in.</summary>
        public string? ReceivedOn { get; set; }
        public string? Customer { get; set; }
        public string? Location { get; set; }
        public int? InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public int? BookingId { get; set; }
        public decimal Amount { get; set; }
        /// <summary>Receipt payment type (Cash, Bank, Cheque...) or the payment method label.</summary>
        public string? Method { get; set; }
    }
}
