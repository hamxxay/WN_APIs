using WorkNest.Application.DTOs.SpaceType;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class SpaceTypeService : ISpaceTypeService
    {
        private readonly IDbRepository _db;
        public SpaceTypeService(IDbRepository db) => _db = db;

        public async Task<IEnumerable<object>> GetAllSpaceTypesAsync() =>
            (await _db.GetAllSpaceTypesAsync()).Cast<object>();

        public async Task<ApiResponse> CreateSpaceTypeAsync(SpaceTypeUpsertRequest request)
        {
            var id = await _db.CreateSpaceTypeAsync(request.Name,
                request.Capacity ?? 0, request.HourlyAllowed ?? false);
            return ApiResponse.Ok(new { id }, "Space type created.");
        }

        public async Task<ApiResponse> UpdateSpaceTypeAsync(string id, SpaceTypeUpsertRequest request)
        {
            await _db.UpdateSpaceTypeAsync(id, request.Name,
                request.Capacity ?? 0, request.HourlyAllowed ?? false);
            return ApiResponse.Ok("Space type updated.");
        }

        public async Task<ApiResponse> DeleteSpaceTypeAsync(string id)
        {
            await _db.SoftDeleteSpaceTypeAsync(id);
            return ApiResponse.Ok("Space type deleted.");
        }
    }
}
