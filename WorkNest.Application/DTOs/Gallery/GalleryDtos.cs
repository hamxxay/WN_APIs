namespace WorkNest.Application.DTOs.Gallery
{
    public class GalleryUpsertRequest
    {
        public string? Title { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public int? SortOrder { get; set; } = 0;
        public bool? IsActive { get; set; } = true;
    }

    public class GalleryDto
    {
        public string? Id { get; set; }
        public int? NumericId { get; set; }
        public string? IdGuid { get; set; }
        public string? Title { get; set; }
        public string? ImageUrl { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }
}
