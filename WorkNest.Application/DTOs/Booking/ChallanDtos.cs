namespace WorkNest.Application.DTOs.Booking
{
    public class ContractDetailsDto
    {
        public DateTime? ContractStartDate { get; set; }
        public DateTime? ContractEndDate { get; set; }
        public int NumberOfMonths { get; set; }
        public decimal MonthlyRent { get; set; }
        public string? BillingPeriod { get; set; }
        public int BillingPeriodMonths { get; set; }
        public decimal CurrentCycleAmount { get; set; }
        public decimal TotalContractAmount { get; set; }
        public DateTime? NextBillingDate { get; set; }
        public DateTime? NextBillDueDate { get; set; }
        public decimal BalanceLeft { get; set; }
        public decimal SecurityDeposit { get; set; }
        public string? SpaceNumber { get; set; }
    }

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
        // â”€â”€ Challan info â”€â”€
        public int ChallanId { get; set; }
        public string? ChallanPublicId { get; set; }
        public string ChallanNumber { get; set; } = string.Empty;
        public DateTime? IssuedOn { get; set; }
        public DateTime? ValidUntil { get; set; }
        public int ChallanStatusId { get; set; }
        public string? ChallanNotes { get; set; }

        // â”€â”€ Booking info â”€â”€
        public int BookingId { get; set; }
        public string? BookingPublicId { get; set; }
        public DateTime? StartOn { get; set; }
        public DateTime? EndOn { get; set; }
        public DateTime? ContractStartDate { get; set; }
        public DateTime? ContractEndDate { get; set; }
        public string? BookingStatusCode { get; set; }
        public string? BookingStatusLabel { get; set; }
        public DateTime? BookedOn { get; set; }

        // â”€â”€ Customer info â”€â”€
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }

        // â”€â”€ Space info â”€â”€
        public string? SpaceCode { get; set; }
        public string? SpaceNumber { get; set; }
        public string? SpaceName { get; set; }
        public int SpaceCapacity { get; set; }
        public string? SpaceTypeName { get; set; }

        // â”€â”€ Location info â”€â”€
        public string? LocationName { get; set; }
        public string? BranchName { get; set; }
        public string? CompanyName { get; set; }

        // â”€â”€ Billing info â”€â”€
        public string? BillingPeriodCode { get; set; }
        public string? BillingPeriodLabel { get; set; }
        public string? BillingPeriod { get; set; }
        public int BillingPeriodMonths { get; set; }
        public int NumberOfMonths { get; set; }
        public decimal SeatPrice { get; set; }
        public decimal RoomPrice { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal CurrentCycleAmount { get; set; }
        public decimal SecurityDeposit { get; set; }
        public decimal DiscountPercentage { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal SubtotalAmount { get; set; }

        // â”€â”€ Financial breakdown (from WN_BookingLines) â”€â”€
        public List<ChallanLineDto> Details { get; set; } = new();

        public decimal TotalPayable { get; set; }
        public decimal TotalContractAmount { get; set; }
        public DateTime? NextBillingDate { get; set; }
        public DateTime? NextBillDueDate { get; set; }
        public decimal BalanceLeft { get; set; }
        public decimal TotalPaidAmount { get; set; }

                public string? TimeSlot { get; set; }
        public ContractDetailsDto? Contract { get; set; }

        public bool IsMeetingRoom =>
            (SpaceTypeName != null && (SpaceTypeName.Contains("Meeting", System.StringComparison.OrdinalIgnoreCase) || SpaceTypeName.Contains("Conference", System.StringComparison.OrdinalIgnoreCase))) ||
            (BillingPeriodMonths <= 0 && TotalContractAmount <= 0) ||
            Contract == null;
    }
}
