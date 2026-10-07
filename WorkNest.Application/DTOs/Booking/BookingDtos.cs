namespace WorkNest.Application.DTOs.Booking
{
    public class BookingDetailsResponseDto
    {
        public int BookingId { get; set; }
        public string? BookingPublicId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerAddress { get; set; }
        public string? SpaceCode { get; set; }
        public string? SpaceName { get; set; }
        public string? SpaceNumber { get; set; }
        public int SpaceCapacity { get; set; }
        public string? SpaceTypeName { get; set; }
        public string? LocationName { get; set; }
        public string? LocationAddress { get; set; }
        public string? CityName { get; set; }
        public string? BranchName { get; set; }
        public string? CompanyName { get; set; }
        public string? BookingStatusCode { get; set; }
        public string? BookingStatusLabel { get; set; }
        public string? BookingStatus { get; set; }
        public DateTime? StartOn { get; set; }
        public DateTime? EndOn { get; set; }
        public DateTime? ContractStartDate { get; set; }
        public DateTime? ContractEndDate { get; set; }
        public int NumberOfMonths { get; set; }
        public decimal MonthlyRent { get; set; }
        public string? BillingPeriod { get; set; }
        public string? BillingPeriodLabel { get; set; }
        public int BillingPeriodMonths { get; set; }
        public decimal CurrentCycleAmount { get; set; }
        public decimal FirstCycleRent { get; set; }
        public decimal TotalContractAmount { get; set; }
        public DateTime? NextBillDueDate { get; set; }
        public DateTime? NextBillingDate { get; set; }
        public decimal BalanceLeft { get; set; }
        public decimal SecurityDeposit { get; set; }
        public string? OfferingType { get; set; } = "24/7";
        public string? ShiftType { get; set; } = "24_7";
        public decimal? PerSeatSupportRate { get; set; } = 2000.00m;
        public byte? SupportChargesId { get; set; }
        public decimal AppliedChargePercentage { get; set; }
        public decimal AppliedTaxPercentage { get; set; }
        public decimal SupportChargeAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal TotalPayable { get; set; }
        public decimal TotalPaidAmount { get; set; }
        public DateTime? BookedOn { get; set; }
        public ContractDetailsDto Contract { get; set; } = new();
        public List<object> Details { get; set; } = new();
    }

    public class AdminBookingRequest
    {
        public string? UserIdGuid { get; set; }
        public int? UserId { get; set; }
        public string? SpaceIdGuid { get; set; }
        public int? SpaceId { get; set; }
        public string? OfferingType { get; set; } = "24/7";
        public string? ShiftType { get; set; }
        public decimal? PerSeatSupportRate { get; set; }
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public string? StartOn { get; set; }
        public string? EndOn { get; set; }
        public string? ContractStartDate { get; set; }
        public string? ContractEndDate { get; set; }
        public string? BillingStartDate { get; set; }
        public string? EffectiveFrom { get; set; }
        public string? Notes { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerName { get; set; }
        public string? Phone { get; set; }
        public string DiscountType { get; set; } = "Percentage";
        public decimal DiscountPercentage { get; set; } = 0;
        public decimal DiscountValue { get; set; } = 0;
        public decimal? SecurityDepositOverride { get; set; }
        public int? FloorId { get; set; }
        public int? BillingPeriodMonths { get; set; }
        public int? SecurityDepositMonths { get; set; }
        public int? AdvanceRentMonths { get; set; }
        public byte? SupportChargesId { get; set; }
        public int? Capacity { get; set; }
        /// <summary>Per-seat monthly price; may only be raised above the space's standard rate (same rule as quotations).</summary>
        public decimal? PerSeatBasePrice { get; set; }
        /// <summary>Generate Withholding Tax (WHT) invoices for this booking; WhtRate is then required (0.01-99.99).</summary>
        public bool SendWhtInvoice { get; set; }
        public decimal? WhtRate { get; set; }
    }

    public class BookingRequest
    {
        public int? SpaceId { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public string? Notes { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? CnicOrPassport { get; set; }
        public string? Address { get; set; }
        public int? CityId { get; set; }
        public byte? SupportChargesId { get; set; }
        public string? ShiftType { get; set; } = "24_7";
        public string? OfferingType { get; set; } = "24/7";
    }

    public class SmartBookingRequest
    {
        public string CategoryCode { get; set; } = string.Empty;
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public int? Capacity { get; set; }
        public string? Notes { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? CnicOrPassport { get; set; }
        public string? Address { get; set; }
        public int? CityId { get; set; }
        public string? ShiftType { get; set; } = "24_7";
        public string? OfferingType { get; set; } = "24/7";
    }

    public class ReassignBookingRequest
    {
        public int NewSpaceId { get; set; }
    }

    public class BookingStatusUpdateRequest
    {
        public byte StatusId { get; set; }
    }

    public class BookingUpdateRequest
    {
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string? Notes { get; set; }
    }

    public class CancelBookingRequest
    {
        public string? CancelReason { get; set; }
    }

    public class AvailableSpacesRequest
    {
        public int SpaceTypeId { get; set; }
        public DateTime StartOn { get; set; }
        public DateTime EndOn { get; set; }
        public int? Capacity { get; set; }
        public string? ShiftType { get; set; } = "24_7";
    }
}