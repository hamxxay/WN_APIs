using WorkNest.Application.DTOs.Amenity;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IAmenityService
    {
        Task<ApiResponse> GetAllAmenitiesAsync();
        Task<ApiResponse> CreateAmenityAsync(AmenityUpsertRequest request);
    }
}
