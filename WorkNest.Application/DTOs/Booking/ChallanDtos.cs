namespace WorkNest.Application.DTOs.Booking
{
    /// <summary>
    /// A single financial line in the challan breakdown (from WN_BookingLines).
    /// </summary>
    public class ChallanLineDto
    {
        public int LineId { get; set; }
        public string ChargeTypeCode { get; set; } = string.Empty;
        public string ChargeTypeLabel { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxRate { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal LineTotal { get; set; }
        public int? AccountId { get; set; }
        public string? AccountName { get; set; }
    }

    /// <summary>
    /// Full challan response including booking context and financial breakdown.
    /// Source of truth: WN_Challans + VW_WN_BookingSummary + WN_BookingLines.
    /// </summary>
    public class ChallanResponseDto
    {
        // ── Challan info ──
        public int ChallanId { get; set; }
        public string? ChallanPublicId { get; set; }
        public string ChallanNumber { get; set; } = string.Empty;
        public DateTime? IssuedOn { get; set; }
        public DateTime? ValidUntil { get; set; }
        public int ChallanStatusId { get; set; }
        public string? ChallanNotes { get; set; }

        // ── Booking info ──
        public int BookingId { get; set; }
        public string? BookingPublicId { get; set; }
        public DateTime? StartOn { get; set; }
        public DateTime? EndOn { get; set; }
        public string? BookingStatusCode { get; set; }
        public string? BookingStatusLabel { get; set; }
        public DateTime? BookedOn { get; set; }

        // ── Customer info ──
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }

        // ── Space info ──
        public string? SpaceCode { get; set; }
        public string? SpaceName { get; set; }
        public int SpaceCapacity { get; set; }
        public string? SpaceTypeName { get; set; }

        // ── Location info ──
        public string? LocationName { get; set; }
        public string? BranchName { get; set; }
        public string? CompanyName { get; set; }

        // ── Billing info ──
        public string? BillingPeriodCode { get; set; }
        public string? BillingPeriodLabel { get; set; }
        public decimal SeatPrice { get; set; }
        public decimal RoomPrice { get; set; }
        public decimal SecurityDeposit { get; set; }

        // ── Financial breakdown (from WN_BookingLines) ──
        public List<ChallanLineDto> Details { get; set; } = new();

        /// <summary>
        /// Total payable = SUM of all LineTotal values.
        /// Computed from the persisted BookingLines, not calculated independently.
        /// </summary>
        public decimal TotalPayable { get; set; }
    }
}
