using WorkNest.Application.DTOs.Gallery;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IGalleryService
    {
        Task<IEnumerable<object>> GetAllImagesAsync(int? locationId);
        Task<(IEnumerable<object> Items, int Total)> GetImagesAsync(int page, int limit, int? locationId);
        Task<ApiResponse> CreateImageAsync(GalleryUpsertRequest request, int? actorId);
        Task<ApiResponse> UpdateImageAsync(int id, GalleryUpdateRequest request);
        Task<ApiResponse> DeleteImageAsync(int id);
    }
}
