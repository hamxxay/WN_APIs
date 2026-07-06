using WorkNest.Application.DTOs.Gallery;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IGalleryService
    {
        Task<IEnumerable<object>> GetAllImagesAsync();
        Task<ApiResponse> CreateImageAsync(GalleryUpsertRequest request);
        Task<ApiResponse> UpdateImageAsync(string id, GalleryUpsertRequest request);
        Task<ApiResponse> DeleteImageAsync(string id);
    }
}
