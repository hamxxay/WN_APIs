using WorkNest.Application.DTOs.Location;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface ILocationService
    {
        Task<IEnumerable<object>> GetAllLocationsAsync();
        Task<ApiResponse> CreateLocationAsync(LocationUpsertRequest request);
        Task<ApiResponse> UpdateLocationAsync(string id, LocationUpsertRequest request);
        Task<ApiResponse> DeleteLocationAsync(string id);
    }
}
