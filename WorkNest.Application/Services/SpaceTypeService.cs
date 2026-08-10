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

        public async Task<(IEnumerable<object> Items, int Total)> GetSpaceTypesAsync(int page, int limit)
        {
            var (rows, total) = await _db.GetSpaceTypesAsync(page, limit);
            return (rows.Cast<object>(), total);
        }

        public async Task<ApiResponse> CreateSpaceTypeAsync(SpaceTypeUpsertRequest request, int? actorId)
        {
            var (id, publicId) = await _db.InsertSpaceTypeAsync(
                request.Name, request.Description, request.CategoryId, request.HourlyAllowed, actorId);
            return ApiResponse.Ok(new { id, publicId }, "Space type created.");
        }

        public async Task<ApiResponse> UpdateSpaceTypeAsync(int id, SpaceTypeUpsertRequest request, int? actorId)
        {
            await _db.UpdateSpaceTypeAsync(id, request.Name, request.Description,
                request.CategoryId, request.HourlyAllowed, actorId);
            return ApiResponse.Ok("Space type updated.");
        }

        public async Task<ApiResponse> DeleteSpaceTypeAsync(int id)
        {
            await _db.DeleteSpaceTypeAsync(id);
            return ApiResponse.Ok("Space type deleted.");
        }
    }
}
