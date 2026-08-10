namespace WorkNest.Application.DTOs.Booking
{
    public class AdminBookingRequest
    {
        public string? UserIdGuid { get; set; }
        public int UserId { get; set; }
        public string? SpaceIdGuid { get; set; }
        public int SpaceId { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public string? Notes { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerName { get; set; }
        public string? Phone { get; set; }
    }

    public class BookingRequest
    {
        public int SpaceId { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public string? Notes { get; set; }
    }

    public class SmartBookingRequest
    {
        public string CategoryCode { get; set; } = string.Empty;
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public int? Capacity { get; set; }
        public string? Notes { get; set; }
    }

    public class ReassignBookingRequest
    {
        public int NewSpaceId { get; set; }
        // NewPricingId is resolved server-side; not accepted from frontend
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
    }
}
