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
    }
}
