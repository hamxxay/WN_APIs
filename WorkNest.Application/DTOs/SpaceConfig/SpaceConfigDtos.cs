namespace WorkNest.Application.DTOs.SpaceConfig
{
    public class SpaceConfigUpdateRequest
    {
        public string? UpdatedBy { get; set; }
        public int? TotalSpaces { get; set; }
        public string? DefaultCapacities { get; set; }
        public string? OpeningTime { get; set; }
        public string? ClosingTime { get; set; }
        public decimal? SecurityDeposit { get; set; }
        public decimal? PricePerHour { get; set; }
        public decimal? PricePerDay { get; set; }
        public decimal? PricePerMonth { get; set; }
    }

    public class SpaceInventoryRequest
    {
        public int LocationId { get; set; }
        public int SpaceTypeId { get; set; }
        public string CodePrefix { get; set; } = string.Empty;
        public int MinCode { get; set; }
        public int TotalSpaces { get; set; }
    }

    public class SpaceConfigV2Request
    {
        public string? SpaceCategory { get; set; }
        public int TotalSpaces { get; set; }
        public string CodePrefix { get; set; } = string.Empty;
        public int MinCode { get; set; }
        public string? OpeningTime { get; set; } = "08:00";
        public string? ClosingTime { get; set; } = "20:00";
        public decimal SecurityDeposit { get; set; }
        public int? RentAccountId { get; set; }
        public int? DepositAccountId { get; set; }
        public int? FloorId { get; set; }
        public decimal PricePerHour { get; set; }
        public decimal PricePerDay { get; set; }
        public decimal PricePerMonth { get; set; }
        public string? Amenities { get; set; }
        public int? LocationId { get; set; }
        public int? SpaceTypeId { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class SpaceConfigDto
    {
        public int? Id { get; set; }
        public string? Category { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class DeleteSpacesFromConfigRequest
    {
        public int ConfigId { get; set; }
        public string? SpaceGuids { get; set; }
    }
}
