namespace WorkNest.Application.DTOs.Membership
{
    public class MembershipCreateRequest
    {
        public int UserId { get; set; }
        public int PlanId { get; set; }
        public DateTime StartOn { get; set; }
        public DateTime? EndOn { get; set; }
        public bool AutoRenew { get; set; }
        public string? Notes { get; set; }
    }

    public class MembershipStatusUpdateRequest
    {
        public byte StatusId { get; set; }
    }
}
