namespace WorkNest.Application.DTOs.Booking
{
    public class GuestDetails
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }

    public class PaymentDetails
    {
        public string Method { get; set; } = string.Empty;
        public double Amount { get; set; }
        public string? VoucherCode { get; set; }
        public string? BankDepositId { get; set; }
        public string? ReferenceNumber { get; set; }
    }

    public class AdminBookingRequest
    {
        public string UserId { get; set; } = string.Empty;
        public string SpaceId { get; set; } = string.Empty;
        public string StartDateTime { get; set; } = string.Empty;
        public string EndDateTime { get; set; } = string.Empty;
        public double? TotalAmount { get; set; }
        public string? Notes { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerCode { get; set; }
        public string? Phone { get; set; }
        public string? CnicOrPassport { get; set; }
        public string? Address { get; set; }
        public int? CityId { get; set; }
    }

    public class BookingRequest
    {
        public object? SpaceId { get; set; }
        public string? SpaceType { get; set; }
        public string StartDateTime { get; set; } = string.Empty;
        public string EndDateTime { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public GuestDetails? Guest { get; set; }
        public PaymentDetails? Payment { get; set; }
        public double? TotalAmount { get; set; }
    }

    public class SmartBookingRequest
    {
        public string SpaceCategory { get; set; } = string.Empty;
        public string StartDateTime { get; set; } = string.Empty;
        public string EndDateTime { get; set; } = string.Empty;
        public int? Capacity { get; set; }
        public string? Notes { get; set; }
        public double? TotalAmount { get; set; }
        public string? PaymentMethod { get; set; }
        public string? PaymentRef { get; set; }
    }

    public class ReassignBookingRequest
    {
        public string SpaceId { get; set; } = string.Empty;
    }

    public class BookingDto
    {
        public string? IdGuid { get; set; }
        public int? Id { get; set; }
        public string? SpaceName { get; set; }
        public string? SpaceTypeName { get; set; }
        public string? StartDateTime { get; set; }
        public string? EndDateTime { get; set; }
        public double TotalAmount { get; set; }
        public string? Notes { get; set; }
        public string? CreatedAt { get; set; }
        public string? BookingStatus { get; set; }
        public string? UserEmail { get; set; }
        public int? AccountId { get; set; }
        public string? AccountDescription { get; set; }
    }
}
