using WorkNest.Application.DTOs.Location;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface ILocationService
    {
        Task<IEnumerable<object>> GetAllLocationsAsync();
        Task<(IEnumerable<object> Items, int Total)> GetLocationsAsync(int page, int limit, string? search);
        Task<ApiResponse> CreateLocationAsync(LocationUpsertRequest request, int? actorId);
        Task<ApiResponse> UpdateLocationAsync(int id, LocationUpdateRequest request);
        Task<ApiResponse> DeleteLocationAsync(int id);
    }
}
