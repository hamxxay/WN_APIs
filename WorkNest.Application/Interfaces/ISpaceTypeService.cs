using WorkNest.Application.DTOs.SpaceType;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface ISpaceTypeService
    {
        /// <summary>Every space type, or only one location's (locationId null = every location).</summary>
        Task<IEnumerable<object>> GetAllSpaceTypesAsync(int? locationId = null);
        Task<(IEnumerable<object> Items, int Total)> GetSpaceTypesAsync(int page, int limit, int? locationId = null);
        /// <summary>One space type's row (with its LocationId), by Id or public id; null when there is none.</summary>
        Task<IDictionary<string, object?>?> FindSpaceTypeAsync(int? id, Guid? publicId);
        /// <summary>A procedure rule (duplicate name at the location, ...) comes back as a failed response with its message.</summary>
        Task<ApiResponse> CreateSpaceTypeAsync(SpaceTypeUpsertRequest request, int? actorId);
        Task<ApiResponse> UpdateSpaceTypeAsync(int id, SpaceTypeUpsertRequest request, int? actorId);
        Task<ApiResponse> DeleteSpaceTypeAsync(int id);
        /// <summary>Space categories (dbo.WN_SpaceCategories: Id, Code, Label) for the Space Types form.</summary>
        Task<IEnumerable<object>> GetSpaceCategoriesAsync();
    }
}
