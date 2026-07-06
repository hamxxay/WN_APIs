namespace WorkNest.Application.DTOs.PricingPlan
{
    public class PricingPlanUpsertRequest
    {
        public string Name { get; set; } = string.Empty;
        public double Price { get; set; }
        public string? BillingCycle { get; set; }
        public int? IncludesHours { get; set; }
        public bool? IsActive { get; set; } = true;
    }

    public class PricingPlanDto
    {
        public int? Id { get; set; }
        public string? IdGuid { get; set; }
        public string? Name { get; set; }
        public double Price { get; set; }
        public string? Description { get; set; }
        public string? BillingCycle { get; set; }
        public int IncludesHours { get; set; }
        public bool IsActive { get; set; }
        public List<object> Features { get; set; } = [];
    }
}
