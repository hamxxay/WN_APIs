namespace WorkNest.Application.DTOs.SpaceType
{
    public class SpaceTypeUpsertRequest
    {
        public string Name { get; set; } = string.Empty;
        public int? Capacity { get; set; }
        public bool? HourlyAllowed { get; set; } = false;
        public bool? IsActive { get; set; } = true;
        public int? RentAccountId { get; set; }
    }

    public class SpaceTypeDto
    {
        public int? Id { get; set; }
        public string? IdGuid { get; set; }
        public string? Name { get; set; }
        public int? Capacity { get; set; }
        public bool HourlyAllowed { get; set; }
        public bool IsActive { get; set; }
    }
}
