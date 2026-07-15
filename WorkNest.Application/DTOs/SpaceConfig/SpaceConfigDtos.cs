namespace WorkNest.Application.DTOs.SpaceConfig
{
    public class SpaceConfigUpdateRequest
    {
        public int TotalSpaces { get; set; }
        public string? DefaultCapacities { get; set; }
        public string? OpeningTime { get; set; }
        public string? ClosingTime { get; set; }
        public double? SecurityDeposit { get; set; }
    }

    public class SpaceInventoryRequest
    {
        public string SpaceCategory { get; set; } = string.Empty;
        public string SpaceTypeId { get; set; } = string.Empty;
        public string LocationId { get; set; } = string.Empty;
        public double? PricePerHour { get; set; } = 0.0;
        public double? PricePerDay { get; set; } = 0.0;
        public double? PricePerMonth { get; set; } = 0.0;
    }

    public class SpaceConfigDto
    {
        public int? Id { get; set; }
        public string? SpaceCategory { get; set; }
        public int TotalSpaces { get; set; }
        public string? CodePrefix { get; set; }
        public int MinCode { get; set; }
        public string? DefaultCapacities { get; set; }
        public string? OpeningTime { get; set; }
        public string? ClosingTime { get; set; }
        public double SecurityDeposit { get; set; }
        public string? UpdatedOn { get; set; }
        public string? UpdatedBy { get; set; }
    }
}
