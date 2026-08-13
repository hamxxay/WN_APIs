namespace WorkNest.Application.DTOs.SpaceType
{
    public class SpaceTypeUpsertRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public byte? CategoryId { get; set; }
        public short? Capacity { get; set; }
        public bool HourlyAllowed { get; set; }
    }

    public class SpaceTypeDto
    {
        public int? Id { get; set; }
        public string? PublicId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public byte? CategoryId { get; set; }
        public short? Capacity { get; set; }
        public bool HourlyAllowed { get; set; }
    }
}
