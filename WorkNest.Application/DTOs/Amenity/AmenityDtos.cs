namespace WorkNest.Application.DTOs.Amenity
{
    public class AmenityUpsertRequest
    {
        public string Name { get; set; } = string.Empty;
    }

    public class AmenityDto
    {
        public int? Id { get; set; }
        public string? Name { get; set; }
    }
}
