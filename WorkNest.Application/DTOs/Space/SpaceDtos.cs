namespace WorkNest.Application.DTOs.Space
{
    public class SpaceInsertRequest
    {
        public string Name { get; set; } = string.Empty;
        public int LocationId { get; set; }
        public int SpaceTypeId { get; set; }
        public string? Code { get; set; }
        public string? Description { get; set; }
        public int? FloorId { get; set; }
        public double? PricePerDay { get; set; }
        public double? PricePerHour { get; set; }
        public string? ImageUrl { get; set; }
        public string? Amenities { get; set; }
    }

    public class SpaceUpdateRequest
    {
        public string? Name { get; set; }
        public int? LocationId { get; set; }
        public int? SpaceTypeId { get; set; }
        public string? Code { get; set; }
        public string? Description { get; set; }
        public int? FloorId { get; set; }
        public double? PricePerDay { get; set; }
        public double? PricePerHour { get; set; }
        public string? ImageUrl { get; set; }
        public string? Amenities { get; set; }
    }

    public class SpaceDto
    {
        public int? Id { get; set; }
        public string? IdGuid { get; set; }
        public string? Name { get; set; }
        public string? Code { get; set; }
        public int? FloorId { get; set; }
        public string? FloorName { get; set; }
        public string? Description { get; set; }
        public int? LocationId { get; set; }
        public string? LocationIdGuid { get; set; }
        public string? SpaceTypeIdGuid { get; set; }
        public string? LocationName { get; set; }
        public string? SpaceTypeName { get; set; }
        public int? Capacity { get; set; }
        public double PricePerDay { get; set; }
        public double PricePerHour { get; set; }
        public string? Amenities { get; set; }
        public string? AmenityIds { get; set; }
        public string? ImageUrl { get; set; }
        public int? Status { get; set; }
        public string? SpaceStatus { get; set; }
    }
}
