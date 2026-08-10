namespace WorkNest.Application.DTOs.PlanFeature
{
    public class PlanFeatureRequest
    {
        public int PlanId { get; set; }
        public string FeatureName { get; set; } = string.Empty;
        public string? FeatureValue { get; set; }
        public short SortOrder { get; set; }
    }

    public class PlanFeatureUpdateRequest
    {
        public string? FeatureName { get; set; }
        public string? FeatureValue { get; set; }
        public short? SortOrder { get; set; }
    }

    public class PlanFeatureDto
    {
        public int? Id { get; set; }
        public string? PublicId { get; set; }
        public int? PlanId { get; set; }
        public string? FeatureName { get; set; }
        public string? FeatureValue { get; set; }
        public short SortOrder { get; set; }
    }
}
