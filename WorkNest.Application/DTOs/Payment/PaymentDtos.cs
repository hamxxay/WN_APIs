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
        public double Amount { get; set; }
    }

    public class CounterPaymentRequest
    {
        public int BookingId { get; set; }
        public double Amount { get; set; }
    }

    public class PaymentCreateRequest
    {
        public int? MembershipId { get; set; }
        public double Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
    }

    public class PaymentStatusUpdateRequest
    {
        public string Status { get; set; } = string.Empty;
        public string? TransactionRef { get; set; }
    }

    public class PaymentDto
    {
        public string? Id { get; set; }
        public string? IdGuid { get; set; }
        public string? UserEmail { get; set; }
        public double Amount { get; set; }
        public string? PaymentMethod { get; set; }
        public string? PaymentStatus { get; set; }
        public string? TransactionRef { get; set; }
        public string? PaidAt { get; set; }
    }
}
