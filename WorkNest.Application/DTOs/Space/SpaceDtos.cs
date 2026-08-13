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
        public string? ImageUrl { get; set; }
        public int Capacity { get; set; }
    }

    public class SpaceUpdateRequest
    {
        public string? Name { get; set; }
        public int? LocationId { get; set; }
        public int? SpaceTypeId { get; set; }
        public string? Code { get; set; }
        public string? Description { get; set; }
        public int? FloorId { get; set; }
        public string? ImageUrl { get; set; }
        public int? Capacity { get; set; }
    }

    public class SpaceDto
    {
        public int? Id { get; set; }
        public string? PublicId { get; set; }
        public string? Name { get; set; }
        public string? Code { get; set; }
        public int? FloorId { get; set; }
        public string? FloorName { get; set; }
        public int? FloorNumber { get; set; }
        public string? Description { get; set; }
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        public int? SpaceTypeId { get; set; }
        public string? SpaceTypeName { get; set; }
        public string? CategoryCode { get; set; }
        public string? CategoryLabel { get; set; }
        public int? Capacity { get; set; }
        public string? ImageUrl { get; set; }
        public bool? IsActive { get; set; }
        public decimal? SeatPrice { get; set; }
        public decimal? RoomPrice { get; set; }
        public decimal? SecurityDeposit { get; set; }
        public string? BillingPeriodCode { get; set; }
        public string? BillingPeriodLabel { get; set; }
        public string? Amenities { get; set; }
        public int? TotalCount { get; set; }
    }
}
