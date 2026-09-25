using WorkNest.Application.DTOs.Space;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface ISpaceService
    {
        Task<(IEnumerable<object> Items, int Total)> GetSpacesAsync(int page, int limit, string? search);
        Task<ApiResponse> GetSpaceSummaryAsync(int id);
        Task<IEnumerable<object>> GetAvailableSpacesAsync(string? shiftType = "24_7");
        Task<ApiResponse> GetAvailableSpacesByTypeAsync(int spaceTypeId, DateTime startOn, DateTime endOn, string? shiftType = "24_7");
        Task<ApiResponse> GetAvailabilityCountsAsync(string? shiftType = "24_7");
        Task<ApiResponse> CreateSpaceAsync(SpaceInsertRequest request, int? actorId);
        Task<ApiResponse> UpdateSpaceAsync(int id, SpaceUpdateRequest request, int? actorId);
        Task<ApiResponse> DeleteSpaceAsync(int id);
    }
}
