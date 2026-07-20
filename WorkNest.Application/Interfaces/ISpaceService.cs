using WorkNest.Application.DTOs.Space;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Space management and availability operations.</summary>
    public interface ISpaceService
    {
        Task<IEnumerable<object>> GetAllSpacesAsync();
        Task<IEnumerable<object>> GetVacantSpacesAsync();
        Task<ApiResponse> GetSpaceSummaryAsync(string id);
        Task<ApiResponse> CreateSpaceAsync(SpaceInsertRequest request);
        Task<ApiResponse> UpdateSpaceAsync(string id, SpaceUpdateRequest request);
        Task<ApiResponse> DeleteSpaceAsync(string id);
        Task<IEnumerable<object>> GetAvailableSpacesAsync();
        Task<ApiResponse> GetAvailableSpacesByTypeAsync(string spaceType, string? start, string? end);
        Task<ApiResponse> GetAvailabilityCountsAsync();
    }
}
