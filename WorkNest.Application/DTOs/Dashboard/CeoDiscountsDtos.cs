namespace WorkNest.Application.DTOs.Dashboard
{
    /// <summary>
    /// GET api/ceo/discounts (DD_01): the discount given on the rooms sold monthly (private offices, shared spaces).
    /// Bookings = the confirmed bookings running in the period, the same ones as the Rooms section (api/ceo/rooms).
    /// Discount a month of a booking = how far it was sold below its expected price (the list price x seats for 24/7,
    /// less the location's shift discount cap for Morning / Evening; counted only when the Rooms section calls it
    /// "under", i.e. more than 0.5% below) plus its fixed PKR discount a month (a booking discount given as an amount,
    /// which the price comparison does not see). A shift booking at the shift price is not a discount; money above the
    /// expected price does not offset anyone's discount. Invoices: the discount on the invoices issued in the period
    /// (WN_Invoices.DiscountTotal, not void), real money: it lowered the invoice total; the same booking discounts as
    /// billed (by billing months, prorated), so it is shown on its own and not added to the monthly figure.
    /// Before DD_01: Available = false with Message, no summary, empty lists.
    /// </summary>
    public class CeoDiscountsDto
    {
        public bool Available { get; set; } = true;
        /// <summary>Set when Available is false: what to do (run DD_01_ceo_discounts).</summary>
        public string? Message { get; set; }
        /// <summary>month | quarter | year | custom</summary>
        public string Period { get; set; } = "month";
        /// <summary>e.g. "October 2026", "Q4 2026", "2026", "1 Oct - 15 Oct 2026".</summary>
        public string PeriodLabel { get; set; } = "";
        /// <summary>UTC, ISO 8601 with Z.</summary>
        public string GeneratedAt { get; set; } = "";
        /// <summary>Custom period: the first and last day of the range, yyyy-MM-dd; null for month / quarter / year.</summary>
        public string? From { get; set; }
        public string? To { get; set; }
        /// <summary>Null when Available is false.</summary>
        public CeoDiscountSummaryDto? Summary { get; set; }
        /// <summary>Locations with bookings or discounted invoices, biggest discount a month first.</summary>
        public List<CeoDiscountGroupDto> ByLocation { get; set; } = new();
        /// <summary>
        /// The person who sold each booking (who made its converted quotation, else who created the booking; Key = user
        /// id, Role = WN_Roles.DisplayName), biggest discount a month first; Key null = no salesperson recorded (or the
        /// customer booked it).
        /// </summary>
        public List<CeoDiscountGroupDto> BySeller { get; set; } = new();
        /// <summary>Always three: 24/7, Morning, Evening (Key = the shift).</summary>
        public List<CeoDiscountGroupDto> ByShift { get; set; } = new();
        /// <summary>The biggest discounts a month (at most 10), biggest first; the records list has them all.</summary>
        public List<CeoDiscountBookingDto> Biggest { get; set; } = new();
    }

    public class CeoDiscountSummaryDto
    {
        /// <summary>Confirmed bookings on the rooms running in the period (the Rooms section's sales).</summary>
        public int Bookings { get; set; }
        /// <summary>Bookings with a list price (compared with the expected price).</summary>
        public int Priced { get; set; }
        /// <summary>Bookings on a room without a list price: not compared (a fixed PKR discount still counts).</summary>
        public int Unpriced { get; set; }
        /// <summary>Bookings with a discount (below the expected price, a fixed PKR discount, or both).</summary>
        public int Discounted { get; set; }
        /// <summary>Expected rent a month of the priced bookings (the Rooms section's expected total).</summary>
        public decimal ExpectedMonthly { get; set; }
        /// <summary>What the discount percentages are of: the expected rent, or the rent before the fixed discount when unpriced.</summary>
        public decimal BaseMonthly { get; set; }
        /// <summary>Rent a month after every discount.</summary>
        public decimal SoldMonthly { get; set; }
        /// <summary>Total discount a month = BelowPriceMonthly + FixedMonthly.</summary>
        public decimal DiscountMonthly { get; set; }
        /// <summary>DiscountMonthly / BaseMonthly, percent (one decimal).</summary>
        public decimal DiscountPct { get; set; }
        /// <summary>DiscountMonthly / the base of the discounted bookings only, percent (one decimal).</summary>
        public decimal AverageDiscountPct { get; set; }
        /// <summary>How far the "under" bookings were sold below their expected price, a month (the Rooms section's under gap).</summary>
        public decimal BelowPriceMonthly { get; set; }
        /// <summary>Bookings sold below the expected price (the Rooms section's under).</summary>
        public int BelowPriceBookings { get; set; }
        /// <summary>Fixed PKR discounts a month (booking discounts given as an amount).</summary>
        public decimal FixedMonthly { get; set; }
        public int FixedBookings { get; set; }
        /// <summary>Bookings that started in the period, and their discount a month (new deals).</summary>
        public int NewBookings { get; set; }
        public int NewDiscounted { get; set; }
        public decimal NewDiscountMonthly { get; set; }
        /// <summary>Discount on the invoices issued in the period (not void).</summary>
        public decimal InvoiceDiscount { get; set; }
        public int InvoicesDiscounted { get; set; }
        /// <summary>Of InvoiceDiscount: on the invoices that are paid now.</summary>
        public decimal InvoiceDiscountPaid { get; set; }
    }

    /// <summary>A location, a seller or a shift.</summary>
    public class CeoDiscountGroupDto
    {
        /// <summary>Location id / user id / shift; null for "no location" / "no salesperson recorded".</summary>
        public string? Key { get; set; }
        public string Name { get; set; } = "";
        /// <summary>Seller only: the role's DisplayName (WN_Roles).</summary>
        public string? Role { get; set; }
        public int Bookings { get; set; }
        public int Discounted { get; set; }
        public decimal BaseMonthly { get; set; }
        public decimal DiscountMonthly { get; set; }
        /// <summary>DiscountMonthly / BaseMonthly, percent (one decimal).</summary>
        public decimal DiscountPct { get; set; }
        /// <summary>The biggest single discount a month in the group.</summary>
        public decimal LargestMonthly { get; set; }
        /// <summary>Location only: discount on the invoices issued in the period.</summary>
        public decimal InvoiceDiscount { get; set; }
        public int InvoicesDiscounted { get; set; }
    }

    /// <summary>One booking with its expected price, what it was sold at and its discount a month.</summary>
    public class CeoDiscountBookingDto
    {
        public int BookingId { get; set; }
        public string? Customer { get; set; }
        public int SpaceId { get; set; }
        public string? Room { get; set; }
        public string? Code { get; set; }
        public int? LocationId { get; set; }
        public string? Location { get; set; }
        public string? SpaceType { get; set; }
        /// <summary>24/7 | Morning | Evening</summary>
        public string Shift { get; set; } = "24/7";
        public int Seats { get; set; }
        /// <summary>yyyy-MM-dd</summary>
        public string? StartOn { get; set; }
        /// <summary>yyyy-MM-dd</summary>
        public string? EndOn { get; set; }
        /// <summary>The booking started in the period (a new deal).</summary>
        public bool StartedInPeriod { get; set; }
        /// <summary>List price per seat (the room's own price, else the booking's pricing row, else today's); null when unknown.</summary>
        public decimal? ListSeatPrice { get; set; }
        /// <summary>List price per seat x seats; null when unknown.</summary>
        public decimal? ListMonthly { get; set; }
        /// <summary>The location's discount cap for the shift, percent (applied to Morning / Evening only).</summary>
        public decimal ShiftDiscountCapPct { get; set; }
        /// <summary>24/7: the list; Morning / Evening: the list less the cap; null when unpriced.</summary>
        public decimal? ExpectedMonthly { get; set; }
        /// <summary>Rent a month after the booking's percentage discount (the Rooms section's sold).</summary>
        public decimal RentMonthly { get; set; }
        /// <summary>Rent a month after every discount (RentMonthly - FixedMonthly).</summary>
        public decimal SoldMonthly { get; set; }
        /// <summary>under | at | over (as the Rooms section); null when unpriced.</summary>
        public string? Pricing { get; set; }
        /// <summary>Expected - rent, a month, when Pricing is under; else 0.</summary>
        public decimal BelowPriceMonthly { get; set; }
        /// <summary>The booking's fixed PKR discount a month (a discount given as an amount); else 0.</summary>
        public decimal FixedMonthly { get; set; }
        /// <summary>BelowPriceMonthly + FixedMonthly.</summary>
        public decimal DiscountMonthly { get; set; }
        /// <summary>DiscountMonthly / BaseMonthly, percent (one decimal).</summary>
        public decimal DiscountPct { get; set; }
        /// <summary>The expected rent, or the rent before the fixed discount when unpriced.</summary>
        public decimal BaseMonthly { get; set; }
        /// <summary>The booking's own discount: its percentage (WN_Bookings.DiscountPercentage).</summary>
        public decimal BookingDiscountPct { get; set; }
        /// <summary>Who sold it (null = not recorded); Seller = WN_Users.Name, SellerRole = WN_Roles.DisplayName.</summary>
        public int? SellerId { get; set; }
        public string? Seller { get; set; }
        public string? SellerRole { get; set; }
    }

    /// <summary>An invoice with a discount whose billing period starts in the period (records/invoices).</summary>
    public class CeoDiscountInvoiceDto
    {
        public int InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public int? BookingId { get; set; }
        public string? Customer { get; set; }
        public int? LocationId { get; set; }
        public string? Location { get; set; }
        public string? Room { get; set; }
        /// <summary>yyyy-MM-dd</summary>
        public string? IssuedOn { get; set; }
        /// <summary>CP_02: yyyy-MM-dd first day of the billing period (as the invoice is placed in the period); null before CP_02.</summary>
        public string? PeriodStart { get; set; }
        /// <summary>CP_02: yyyy-MM-dd last day of the billing period; null when the invoice has none.</summary>
        public string? PeriodEnd { get; set; }
        /// <summary>Before the discount.</summary>
        public decimal SubTotal { get; set; }
        public decimal Discount { get; set; }
        public decimal Total { get; set; }
        public string? Status { get; set; }
        public bool Paid { get; set; }
    }

    /// <summary>GET api/ceo/discounts/records/{kind}: at most 200 rows (Total = all that match), with their totals.</summary>
    public class CeoDiscountRecordsDto<T>
    {
        public bool Available { get; set; } = true;
        public string Title { get; set; } = "";
        public int Total { get; set; }
        /// <summary>bookings: their discount a month; invoices: their discount.</summary>
        public decimal DiscountTotal { get; set; }
        /// <summary>bookings: the base of those bookings a month; invoices: their totals before the discount.</summary>
        public decimal BaseTotal { get; set; }
        /// <summary>DiscountTotal / BaseTotal, percent (one decimal).</summary>
        public decimal DiscountPct { get; set; }
        public List<T> Rows { get; set; } = new();
    }

    /// <summary>Query of GET api/ceo/discounts/records/{kind} (bookings | invoices).</summary>
    public class CeoDiscountRecordsQuery
    {
        public string? Period { get; set; }
        /// <summary>period = custom: the dashboard's range, yyyy-MM-dd (from / to are taken when these are missing).</summary>
        public string? PeriodFrom { get; set; }
        public string? PeriodTo { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        /// <summary>A location id, or "none" (not tied to a location); empty = all locations.</summary>
        public string? LocationId { get; set; }
        /// <summary>bookings: a user id, or "none" (no salesperson recorded).</summary>
        public string? SellerId { get; set; }
        /// <summary>bookings: 24/7 | Morning | Evening.</summary>
        public string? Shift { get; set; }
        /// <summary>bookings: only the ones that started in the period.</summary>
        public bool? NewOnly { get; set; }
        /// <summary>bookings: every booking, not only the discounted ones.</summary>
        public bool? All { get; set; }
    }
}
