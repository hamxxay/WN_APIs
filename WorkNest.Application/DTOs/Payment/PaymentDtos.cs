namespace WorkNest.Application.DTOs.Payment
{
    public class CardPaymentRequest
    {
        public int BookingId { get; set; }
        public string CardHolderName { get; set; } = string.Empty;
        public string CardNumber { get; set; } = string.Empty;
        public string ExpiryMonth { get; set; } = string.Empty;
        public string ExpiryYear { get; set; } = string.Empty;
        public string Cvv { get; set; } = string.Empty;
    }

    public class PayFastInitiateRequest
    {
        public int BookingId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
    }

    public class VoucherGenerateRequest
    {
        public int BookingId { get; set; }
        public decimal Amount { get; set; }
        public DateTime ExpiresOn { get; set; }
    }

    public class PaymentCreateRequest
    {
        public int? BookingId { get; set; }
        public byte PaymentMethodId { get; set; }
        public decimal Amount { get; set; }
        public string? Notes { get; set; }
    }

    public class PaymentStatusUpdateRequest
    {
        public byte StatusId { get; set; }
    }

    public class AdvanceInvoiceRequest
    {
        public int BookingId { get; set; }
        public decimal Amount { get; set; }
        public byte PaymentMethodId { get; set; }
        public string? Notes { get; set; }
    }

    public class AdvanceInvoiceMonthDto
    {
        public string MonthName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class AdvanceInvoicePdfDto
    {
        public string? InvoiceNumber { get; set; }
        public DateTime IssuedOn { get; set; }
        public DateTime? DueOn { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerCode { get; set; }
        public string? SpaceName { get; set; }
        public string? SpaceTypeName { get; set; }
        public string? LocationName { get; set; }
        public DateTime StartOn { get; set; }
        public DateTime EndOn { get; set; }
        public int AdvanceRentMonths { get; set; }
        public decimal AdvanceRentTotal { get; set; }
        public int SecurityDepositMonths { get; set; }
        public decimal SecurityDepositTotal { get; set; }
        public decimal SupportChargeTotal { get; set; }
        public decimal AppliedTaxPercentage { get; set; } = 16.00m;
        public decimal TaxTotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TotalPayable { get; set; }
        public string? Notes { get; set; }
        public List<AdvanceInvoiceMonthDto> MonthsBreakdown { get; set; } = new();
    }

    public class PaymentDto
    {
        public int? Id { get; set; }
        public string? PublicId { get; set; }
        public string? UserEmail { get; set; }
        public decimal Amount { get; set; }
        public string? PaymentMethod { get; set; }
        public string? PaymentStatus { get; set; }
        public string? Notes { get; set; }
        public string? CreatedOn { get; set; }
        public int? BookingId { get; set; }
        public string? SpaceNumber { get; set; }
        public DateTime? ContractStartDate { get; set; }
        public DateTime? ContractEndDate { get; set; }
        public string? BillingPeriod { get; set; }
        public int BillingPeriodMonths { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal CurrentCycleAmount { get; set; }
        public decimal TotalContractAmount { get; set; }
        public decimal TotalPaidAmount { get; set; }
        public decimal BalanceLeft { get; set; }
        public DateTime? NextBillDueDate { get; set; }
        public decimal SecurityDeposit { get; set; }
        public WorkNest.Application.DTOs.Booking.ContractDetailsDto? Contract { get; set; }
    }
}
