using WorkNest.Application.DTOs.Space;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface ISpaceService
    {
        Task<(IEnumerable<object> Items, int Total)> GetSpacesAsync(int page, int limit, string? search);
        Task<ApiResponse> GetSpaceSummaryAsync(int id);
        Task<IEnumerable<object>> GetAvailableSpacesAsync();
        Task<ApiResponse> GetAvailableSpacesByTypeAsync(int spaceTypeId, DateTime startOn, DateTime endOn);
        Task<ApiResponse> GetAvailabilityCountsAsync();
        Task<ApiResponse> CreateSpaceAsync(SpaceInsertRequest request, int? actorId);
        Task<ApiResponse> UpdateSpaceAsync(int id, SpaceUpdateRequest request, int? actorId);
        Task<ApiResponse> DeleteSpaceAsync(int id);
    }
}
