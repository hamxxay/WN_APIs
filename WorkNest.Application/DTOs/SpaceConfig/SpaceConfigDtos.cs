namespace WorkNest.Application.DTOs.SpaceConfig
{
    // ── Legacy (kept for backward compat) ────────────────────
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
        public string? CodePrefix { get; set; }
        public double? PricePerHour { get; set; } = 0.0;
        public double? PricePerDay { get; set; } = 0.0;
        public double? PricePerMonth { get; set; } = 0.0;
        public string? Amenities { get; set; }
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

    // ── New multi-location DTOs ───────────────────────────────

    public class SpaceConfigCreateRequest
    {
        public string SpaceCategory { get; set; } = string.Empty;
        public int TotalSpaces { get; set; }
        public string CodePrefix { get; set; } = string.Empty;
        public int MinCode { get; set; }
        public string? OpeningTime { get; set; }
        public string? ClosingTime { get; set; }
        public double? SecurityDeposit { get; set; }
        public int? RentAccountId { get; set; }
        public int? DepositAccountId { get; set; }
        public int? FloorId { get; set; }
        public double? PricePerHour { get; set; }
        public double? PricePerDay { get; set; }
        public double? PricePerMonth { get; set; }
        public string? Amenities { get; set; }
        public int? LocationId { get; set; }
        public int? BranchId { get; set; }
        public int? CompanyId { get; set; }
        public int? SpaceTypeId { get; set; }
    }

    public class SpaceConfigEditRequest
    {
        public int TotalSpaces { get; set; }
        public string CodePrefix { get; set; } = string.Empty;
        public int MinCode { get; set; }
        public string? OpeningTime { get; set; }
        public string? ClosingTime { get; set; }
        public double? SecurityDeposit { get; set; }
        public int? RentAccountId { get; set; }
        public int? DepositAccountId { get; set; }
        public int? FloorId { get; set; }
        public double? PricePerHour { get; set; }
        public double? PricePerDay { get; set; }
        public double? PricePerMonth { get; set; }
        public string? Amenities { get; set; }
    }

    public class SpaceConfigFilterRequest
    {
        public int? CompanyId { get; set; }
        public int? BranchId { get; set; }
        public int? LocationId { get; set; }
        public int? SpaceTypeId { get; set; }
    }

    public class DeleteSpacesRequest
    {
        public int ConfigId { get; set; }
        /// <summary>Comma-separated GUIDs. Null = delete all.</summary>
        public string? SpaceGuids { get; set; }
    }
}
