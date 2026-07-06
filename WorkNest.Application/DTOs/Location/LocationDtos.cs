namespace WorkNest.Application.DTOs.Location
{
    public class LocationUpsertRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public int? CityId { get; set; }
        public string? OpeningTime { get; set; }
        public string? ClosingTime { get; set; }
        public bool? IsActive { get; set; } = true;
        public int? BranchId { get; set; }
    }

    public class LocationDto
    {
        public int? Id { get; set; }
        public string? IdGuid { get; set; }
        public string? Name { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public int? CityId { get; set; }
        public string? CityName { get; set; }
        public int? BranchId { get; set; }
        public string? BranchName { get; set; }
        public string? BranchCode { get; set; }
    }
}
