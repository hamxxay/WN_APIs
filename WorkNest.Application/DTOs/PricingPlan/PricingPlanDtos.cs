namespace WorkNest.Application.DTOs.PricingPlan
{
    public class PricingPlanUpsertRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public byte BillingPeriodId { get; set; }
        public decimal Price { get; set; }
        public int? IncludesHours { get; set; }
        public string CurrencyCode { get; set; } = "PKR";
    }

    public class PricingPlanDto
    {
        public int? Id { get; set; }
        public string? PublicId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public byte BillingPeriodId { get; set; }
        public int? IncludesHours { get; set; }
        public string CurrencyCode { get; set; } = "PKR";
        public List<object> Features { get; set; } = [];
    }
}
