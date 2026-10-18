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
        /// <summary>The booking's stored Security Deposit (Months) (0 = no deposit).</summary>
        public int SecurityDepositMonths { get; set; }
        public int? OfferingTypeId { get; set; }
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
        public string? ChallanNumber { get; set; }
        public DateTime? ChallanValidUntil { get; set; }
        /// <summary>
        /// False until the database has the corrected billing rules (QA_02 script) or when the booking has no invoice
        /// to bill from: NextBillDueDate, NextBillingDate, BalanceLeft and TotalContractAmount should then not be shown
        /// to customers.
        /// </summary>
        public bool BillingFiguresReliable { get; set; }
        /// <summary>Door access to the space is suspended right now because a bill is unpaid (WN_HIK_BookingAccessSuspensions).</summary>
        public bool AccessSuspended { get; set; }
        /// <summary>Plain-language reason for the customer (which bill, since when).</summary>
        public string? AccessSuspendedReason { get; set; }
        /// <summary>The unpaid invoice behind the suspension (null when it is the booking challan).</summary>
        public int? AccessSuspendedInvoiceId { get; set; }
        public string? AccessSuspendedInvoiceNumber { get; set; }
        public ContractDetailsDto Contract { get; set; } = new();
        public List<object> Details { get; set; } = new();
    }

    public class AdminBookingRequest
    {
        public string? UserIdGuid { get; set; }
        public int? UserId { get; set; }
        public string? SpaceIdGuid { get; set; }
        public int? SpaceId { get; set; }
        public int? OfferingTypeId { get; set; }
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
        /// <summary>No longer used: the deposit is worked out from SecurityDepositMonths (kept so older clients still bind).</summary>
        public decimal? SecurityDepositOverride { get; set; }
        public int? FloorId { get; set; }
        public int? BillingPeriodMonths { get; set; }
        /// <summary>Security Deposit (Months): 0, 1, 2 or 3 (anything else is rejected). Null = the space kind's default
        /// (private office 2). Shared spaces and meeting rooms are always 0.</summary>
        public int? SecurityDepositMonths { get; set; }
        public int? AdvanceRentMonths { get; set; }
        public byte? SupportChargesId { get; set; }
        public int? Capacity { get; set; }
        /// <summary>Per-seat monthly price; may only be raised above the space's standard rate (same rule as quotations).</summary>
        public decimal? PerSeatBasePrice { get; set; }
        /// <summary>Generate Withholding Tax (WHT) invoices for this booking; WhtRate (the WN_WHTaxRate Id) is then required.</summary>
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
        public int? OfferingTypeId { get; set; }
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
        public int? OfferingTypeId { get; set; }
        public string? OfferingType { get; set; } = "24/7";
        /// <summary>
        /// The location the customer chose (WN_Locations.Id): the booking gets a space of this location only.
        /// Null = any location (the old behaviour). Location-bound staff get their own location ([ValidateLocationScope]).
        /// </summary>
        public int? LocationId { get; set; }
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
        /// <summary>
        /// Security Deposit (Months), 0-3 (null = unchanged). A new value recomputes the booking's deposit (months x
        /// monthly rent x (1 - rent discount)) for private offices and shared spaces; meeting rooms stay 0. Not changeable once the
        /// booking has an invoice.
        /// </summary>
        public int? SecurityDepositMonths { get; set; }
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