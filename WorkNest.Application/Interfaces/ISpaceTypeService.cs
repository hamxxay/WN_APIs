using WorkNest.Application.DTOs.SpaceType;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface ISpaceTypeService
    {
        Task<IEnumerable<object>> GetAllSpaceTypesAsync();
        Task<(IEnumerable<object> Items, int Total)> GetSpaceTypesAsync(int page, int limit);
        Task<ApiResponse> CreateSpaceTypeAsync(SpaceTypeUpsertRequest request, int? actorId);
        Task<ApiResponse> UpdateSpaceTypeAsync(int id, SpaceTypeUpsertRequest request, int? actorId);
        Task<ApiResponse> DeleteSpaceTypeAsync(int id);
    }
}
