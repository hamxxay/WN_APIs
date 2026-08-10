using WorkNest.Application.DTOs.Gallery;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class GalleryService : IGalleryService
    {
        private readonly IDbRepository _db;
        public GalleryService(IDbRepository db) => _db = db;

        public async Task<IEnumerable<object>> GetAllImagesAsync(int? locationId) =>
            (await _db.GetAllGalleryImagesAsync(locationId)).Cast<object>();

        public async Task<(IEnumerable<object> Items, int Total)> GetImagesAsync(int page, int limit, int? locationId)
        {
            var (rows, total) = await _db.GetGalleryImagesAsync(page, limit, locationId);
            return (rows.Cast<object>(), total);
        }

        public async Task<ApiResponse> CreateImageAsync(GalleryUpsertRequest request, int? actorId)
        {
            var (id, publicId) = await _db.InsertGalleryImageAsync(
                request.LocationId, request.SpaceId, request.Title,
                request.Description, request.ImageUrl, request.SortOrder, actorId);
            return ApiResponse.Ok(new { id, publicId }, "Gallery image created.");
        }

        public async Task<ApiResponse> UpdateImageAsync(int id, GalleryUpdateRequest request)
        {
            await _db.UpdateGalleryImageAsync(id, request.Title, request.Description, request.ImageUrl, request.SortOrder);
            return ApiResponse.Ok("Gallery image updated.");
        }

        public async Task<ApiResponse> DeleteImageAsync(int id)
        {
            await _db.DeleteGalleryImageAsync(id);
            return ApiResponse.Ok("Gallery image deleted.");
        }
    }
}
