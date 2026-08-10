namespace WorkNest.Application.DTOs.Gallery
{
    public class GalleryUpsertRequest
    {
        public int? LocationId { get; set; }
        public int? SpaceId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }

    public class GalleryUpdateRequest
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public int? SortOrder { get; set; }
    }

    public class GalleryDto
    {
        public int? Id { get; set; }
        public string? PublicId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public int SortOrder { get; set; }
    }
}
