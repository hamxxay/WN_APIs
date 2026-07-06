namespace WorkNest.Application.DTOs.Floor
{
    public class FloorUpsertRequest
    {
        public int LocationId { get; set; }
        public string FloorName { get; set; } = string.Empty;
    }

    public class FloorDto
    {
        public int? Id { get; set; }
        public int? LocationId { get; set; }
        public string? FloorName { get; set; }
    }
}
