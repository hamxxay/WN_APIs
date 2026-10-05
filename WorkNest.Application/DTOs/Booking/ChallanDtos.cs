using System.Text.Json.Serialization;

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
        [JsonIgnore]
        public decimal TotalContractAmount { get; set; }
        public DateTime? NextBillingDate { get; set; }
        public DateTime? NextBillDueDate { get; set; }
        public decimal BalanceLeft { get; set; }
        public decimal SecurityDeposit { get; set; }
        public string? SpaceNumber { get; set; }
        public decimal AppliedTaxPercentage { get; set; } = 16.00m;
        public decimal TaxAmount { get; set; }
        public decimal TaxAmountOnAdvanceRent { get; set; }
        public decimal TaxAmountOnContract { get; set; }
    }

    public class ChallanFieldDto
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string? Key { get; set; }
        public bool IsHeader { get; set; }
    }

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

    public class ChallanResponseDto
    {
        public int ChallanId { get; set; }
        public string? ChallanPublicId { get; set; }
        public string ChallanNumber { get; set; } = string.Empty;
        public DateTime? IssuedOn { get; set; }
        public DateTime? ValidUntil { get; set; }
        public int ChallanStatusId { get; set; }
        public string? ChallanNotes { get; set; }

        public string SpaceType { get; set; } = string.Empty;
        public string BillingType { get; set; } = string.Empty;
        public List<ChallanFieldDto> Fields { get; set; } = new();

        public int BookingId { get; set; }
        public string? BookingPublicId { get; set; }
        public DateTime? StartOn { get; set; }
        public DateTime? EndOn { get; set; }
        public DateTime? ContractStartDate { get; set; }
        public DateTime? ContractEndDate { get; set; }
        public string? BookingStatusCode { get; set; }
        public string? BookingStatusLabel { get; set; }
        public DateTime? BookedOn { get; set; }

        public string? CustomerName { get; set; }
        public string? CustomerCompany { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerAddress { get; set; }

        public string? SpaceCode { get; set; }
        public string? SpaceNumber { get; set; }
        public string? SpaceName { get; set; }
        public int SpaceCapacity { get; set; }
        public string? SpaceTypeName { get; set; }

        public string? LocationName { get; set; }
        public string? LocationAddress { get; set; }
        public string? CityName { get; set; }
        public string? BranchName { get; set; }
        public string? CompanyName { get => CustomerCompany; set => CustomerCompany = value; }

        public string? BillingPeriodCode { get; set; }
        public string? BillingPeriodLabel { get; set; }
        public string? BillingPeriod { get; set; }
        public int BillingPeriodMonths { get; set; }
        public int NumberOfMonths { get; set; }
        public decimal SeatPrice { get; set; }
        public decimal RoomPrice { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal FirstCycleRent { get; set; }
        public decimal CurrentCycleAmount { get; set; }
        public decimal SecurityDeposit { get; set; }
        public decimal SupportChargeAmount { get; set; }
        public decimal DiscountPercentage { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal SubtotalAmount { get; set; }
        public decimal AppliedTaxPercentage { get; set; } = 16.00m;
        public decimal TaxAmount { get; set; }
        public decimal TaxAmountOnAdvanceRent { get; set; }
        public decimal TaxAmountOnContract { get; set; }
        public decimal WithholdingTaxRate { get; set; } = 15.00m;

        public List<ChallanLineDto> Details { get; set; } = new();

        public decimal TotalPayable { get; set; }
        [JsonIgnore]
        public decimal TotalContractAmount { get; set; }
        public DateTime? NextBillingDate { get; set; }
        public DateTime? NextBillDueDate { get; set; }
        public decimal BalanceLeft { get; set; }
        public decimal TotalPaidAmount { get; set; }

        public string? TimeSlot { get; set; }
        public ContractDetailsDto? Contract { get; set; }

        public bool IsMeetingRoom =>
            string.Equals(SpaceType, "MeetingRoom", System.StringComparison.OrdinalIgnoreCase) ||
            (SpaceTypeName != null && (SpaceTypeName.Contains("Meeting", System.StringComparison.OrdinalIgnoreCase) || SpaceTypeName.Contains("Conference", System.StringComparison.OrdinalIgnoreCase))) ||
            (BillingPeriodMonths <= 0 && TotalContractAmount <= 0) ||
            Contract == null;
    }
}