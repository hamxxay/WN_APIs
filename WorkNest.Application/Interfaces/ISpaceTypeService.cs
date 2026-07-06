using WorkNest.Application.DTOs.SpaceType;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface ISpaceTypeService
    {
        Task<IEnumerable<object>> GetAllSpaceTypesAsync();
        Task<ApiResponse> CreateSpaceTypeAsync(SpaceTypeUpsertRequest request);
        Task<ApiResponse> UpdateSpaceTypeAsync(string id, SpaceTypeUpsertRequest request);
        Task<ApiResponse> DeleteSpaceTypeAsync(string id);
    }
}
