namespace WorkNest.Application.DTOs.Dashboard
{
    // Main admin dashboard (/admin/dashboard), DASH_01: the space type cards, their tag lists and the Space Operations
    // & Inventory table, counted on the server (dbo.WN_Dash_*) instead of from the first 1,000 rows in the browser.
    // Date-times are business (Pakistan) time without a zone, "yyyy-MM-ddTHH:mm:ss"; dates "yyyy-MM-dd".

    /// <summary>GET api/dashboard/main-summary: one card per space type.</summary>
    public class MainDashboardSummaryDto
    {
        /// <summary>The Expiring Soon window the counts use, in days.</summary>
        public int ExpiringDays { get; set; }
        /// <summary>Every space in scope (the "All Types (N Spaces)" button).</summary>
        public int TotalSpaces { get; set; }
        public List<SpaceTypeCardDto> Cards { get; set; } = new();
        public string GeneratedAt { get; set; } = "";
    }

    public class SpaceTypeCardDto
    {
        public int SpaceTypeId { get; set; }
        public string SpaceTypeName { get; set; } = "";
        /// <summary>The location the space type belongs to (null: an older type without one).</summary>
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        public int TotalSpaces { get; set; }
        /// <summary>Spaces with a live booking now.</summary>
        public int BookedCount { get; set; }
        /// <summary>Spaces without a live booking now.</summary>
        public int VacantCount { get; set; }
        /// <summary>Spaces with an active quotation (booked or not).</summary>
        public int QuotedCount { get; set; }
        /// <summary>Live bookings now (on these spaces) that end within the Expiring Soon window.</summary>
        public int ExpiringCount { get; set; }
    }

    /// <summary>GET api/dashboard/main-summary/details: one tag of a card, one page.</summary>
    public class SpaceTypeDetailsDto
    {
        /// <summary>booked | vacant | quoted | expiring</summary>
        public string Tag { get; set; } = "";
        public int SpaceTypeId { get; set; }
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<SpaceTypeDetailRowDto> Items { get; set; } = new();
    }

    /// <summary>A row of a tag list; each tag fills the fields it shows.</summary>
    public class SpaceTypeDetailRowDto
    {
        public int SpaceId { get; set; }
        public string SpaceCode { get; set; } = "";
        public string SpaceName { get; set; } = "";
        public int Capacity { get; set; }
        public string? LocationName { get; set; }
        /// <summary>booked / expiring: the booking (expiring: each booking that ends soon).</summary>
        public int? BookingId { get; set; }
        public string? BookingStart { get; set; }
        public string? BookingEnd { get; set; }
        public string? BookingStatus { get; set; }
        public string? CustomerName { get; set; }
        public string? CompanyName { get; set; }
        /// <summary>booked: which shifts the live bookings on the space take now.</summary>
        public bool Has247 { get; set; }
        public bool HasMorning { get; set; }
        public bool HasEvening { get; set; }
        /// <summary>vacant: the next live booking that starts later.</summary>
        public string? NextBookingDate { get; set; }
        public string? NextBookingCustomer { get; set; }
        /// <summary>quoted: the active quotation.</summary>
        public int? QuotationId { get; set; }
        public string? QuotationNumber { get; set; }
        public string? QuotationStatus { get; set; }
        public string? QuotationDate { get; set; }
        public string? QuotationExpiryDate { get; set; }
        public decimal? QuotationAmount { get; set; }
        /// <summary>quoted: the space is booked as well.</summary>
        public bool IsAlsoBooked { get; set; }
        /// <summary>expiring: whole days left (rounded up).</summary>
        public int? DaysRemaining { get; set; }
    }

    /// <summary>Query of GET api/dashboard/inventory (the Space Operations &amp; Inventory table).</summary>
    public class MainDashboardInventoryQuery
    {
        /// <summary>The dashboard's location filter (comma-separated); none = every location the user may see.</summary>
        public string? LocationIds { get; set; }
        /// <summary>The table's own Location filter (within the dashboard's locations).</summary>
        public int? Location { get; set; }
        /// <summary>Space code or name contains.</summary>
        public string? Search { get; set; }
        /// <summary>Customer or company contains.</summary>
        public string? Customer { get; set; }
        /// <summary>Space type name (as on the cards).</summary>
        public string? SpaceType { get; set; }
        /// <summary>Booked | Quoted | Available; none = all.</summary>
        public string? Status { get; set; }
        public bool PrivateOnly { get; set; }
        public int? ExpiringDays { get; set; }
        /// <summary>urgency (default) | spaceCode | customer | bookingStart | bookingEnd | remainingTime | status</summary>
        public string? SortBy { get; set; }
        /// <summary>asc (default) | desc; urgency is always ascending.</summary>
        public string? SortDir { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }

    /// <summary>GET api/dashboard/inventory: one page of the table.</summary>
    public class InventoryPageDto
    {
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<InventoryRowDto> Items { get; set; } = new();
    }

    public class InventoryRowDto
    {
        public int SpaceId { get; set; }
        public string SpaceCode { get; set; } = "";
        public string SpaceName { get; set; } = "";
        public int Capacity { get; set; }
        public int SpaceTypeId { get; set; }
        public string SpaceTypeName { get; set; } = "";
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        /// <summary>Booked | Quoted | Available</summary>
        public string Status { get; set; } = "";
        public int? BookingId { get; set; }
        public string? CustomerName { get; set; }
        public string? CompanyName { get; set; }
        public string? BookingStart { get; set; }
        public string? BookingEnd { get; set; }
        public string? BookingStatus { get; set; }
        /// <summary>24/7 | Morning | Evening (booked spaces).</summary>
        public string? ShiftType { get; set; }
        public int? QuotationId { get; set; }
        public string? QuotationNumber { get; set; }
        public string? QuotationCustomerName { get; set; }
        public string? QuotationDate { get; set; }
        public string? QuotationExpiryDate { get; set; }
        public decimal? QuotationAmount { get; set; }
        public string? QuotationStatus { get; set; }
        /// <summary>Whole days left on the booking shown (rounded up); 99999 when not booked.</summary>
        public int DaysRemaining { get; set; }
        public bool IsExpiringSoon { get; set; }
        public bool IsPrivateSpace { get; set; }
    }
}
