namespace WorkNest.Application.DTOs.PlanFeature
{
    public class PlanFeatureRequest
    {
        public int PlanId { get; set; }
        public string FeatureName { get; set; } = string.Empty;
    }

    public class PlanFeatureDto
    {
        public int? Id { get; set; }
        public int? PlanId { get; set; }
        public string? FeatureName { get; set; }
    }
}
