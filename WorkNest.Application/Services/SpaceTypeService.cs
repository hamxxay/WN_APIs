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
            (await _db.GetAllSpaceTypesAsync()).Select(r => (object)new
            {
                id            = r.TryGetValue("Id",           out var i)   ? i   : r.TryGetValue("id", out var i2) ? i2 : null,
                idGuid        = r.TryGetValue("IdGUID",       out var g)   ? g?.ToString() : r.TryGetValue("idGuid", out var g2) ? g2?.ToString() : null,
                name          = r.TryGetValue("Name",         out var n)   ? n?.ToString() : r.TryGetValue("name", out var n2) ? n2?.ToString() : null,
                capacity      = r.TryGetValue("Capacity",     out var c)   ? c   : r.TryGetValue("capacity", out var c2) ? c2 : null,
                hourlyAllowed = r.TryGetValue("HourlyAllowed",out var h)   ? h   : r.TryGetValue("hourlyAllowed", out var h2) ? h2 : null,
                isActive      = r.TryGetValue("IsActive",     out var a)   ? a   : r.TryGetValue("isActive", out var a2) ? a2 : null,
                rentAccountId = r.TryGetValue("RentAccountId",out var ra)  ? ra  : r.TryGetValue("rentAccountId", out var ra2) ? ra2 : null,
            });

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
            if (request.RentAccountId.HasValue)
                await _db.BulkUpdateSpaceRentAccountAsync(id, request.RentAccountId.Value);
            return ApiResponse.Ok("Space type updated.");
        }

        public async Task<ApiResponse> DeleteSpaceTypeAsync(string id)
        {
            await _db.SoftDeleteSpaceTypeAsync(id);
            return ApiResponse.Ok("Space type deleted.");
        }
    }
}
