namespace WorkNest.Application.DTOs.Membership
{
    public class MembershipCreateRequest
    {
        public string? UserId { get; set; }
        public int PlanId { get; set; }
        public string StartDate { get; set; } = string.Empty;
    }

    public class MembershipDto
    {
        public int? Id { get; set; }
        public string? IdGuid { get; set; }
        public string? UserEmail { get; set; }
        public string? PlanName { get; set; }
        public double PlanPrice { get; set; }
        public string? PlanCycle { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public object? Status { get; set; }
    }
}
