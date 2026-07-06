using WorkNest.Application.DTOs.Gallery;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class GalleryService : IGalleryService
    {
        private readonly IDbRepository _db;
        public GalleryService(IDbRepository db) => _db = db;

        public async Task<IEnumerable<object>> GetAllImagesAsync() =>
            (await _db.GetAllGalleryImagesAsync()).Cast<object>();

        public async Task<ApiResponse> CreateImageAsync(GalleryUpsertRequest request)
        {
            var id = await _db.CreateGalleryImageAsync(request.Title, request.ImageUrl,
                request.SortOrder ?? 0, request.IsActive ?? true);
            return ApiResponse.Ok(new { id }, "Gallery image created.");
        }

        public async Task<ApiResponse> UpdateImageAsync(string id, GalleryUpsertRequest request)
        {
            await _db.UpdateGalleryImageAsync(id, request.Title, request.ImageUrl,
                request.SortOrder ?? 0, request.IsActive ?? true);
            return ApiResponse.Ok("Gallery image updated.");
        }

        public async Task<ApiResponse> DeleteImageAsync(string id)
        {
            await _db.SoftDeleteGalleryImageAsync(id);
            return ApiResponse.Ok("Gallery image deleted.");
        }
    }
}
