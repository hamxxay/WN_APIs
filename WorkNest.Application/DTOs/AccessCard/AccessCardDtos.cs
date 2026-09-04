namespace WorkNest.Application.DTOs.AccessCard
{
    public class AccessCardRequest
    {
        public int LocationId { get; set; }
        public int CustomerId { get; set; }
        public int BookingId { get; set; }
        public int SpaceId { get; set; }
        public string? CardNumber { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int Status { get; set; } = 1; // 1 = Active, 2 = Restricted, 3 = Inactive
    }
}
