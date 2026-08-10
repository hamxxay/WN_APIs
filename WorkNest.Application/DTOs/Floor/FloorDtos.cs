namespace WorkNest.Application.DTOs.Floor
{
    public class FloorUpsertRequest
    {
        public int LocationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public short FloorNumber { get; set; }
    }

    public class FloorDto
    {
        public int? Id { get; set; }
        public int? LocationId { get; set; }
        public string? Name { get; set; }
        public short FloorNumber { get; set; }
    }
}
