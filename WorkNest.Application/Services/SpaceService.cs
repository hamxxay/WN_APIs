using WorkNest.Application.DTOs.Space;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class SpaceService : ISpaceService
    {
        private readonly IDbRepository _db;
        public SpaceService(IDbRepository db) => _db = db;

        public async Task<(IEnumerable<object> Items, int Total)> GetSpacesAsync(int page, int limit, string? search)
        {
            var (rows, total) = await _db.GetSpacesAsync(page, limit, search);
            return (rows.Cast<object>(), total);
        }

        public async Task<IEnumerable<object>> GetAvailableSpacesAsync() =>
            (await _db.GetAvailableSpacesAsync()).Cast<object>();

        public async Task<ApiResponse> GetAvailableSpacesByTypeAsync(int spaceTypeId, DateTime startOn, DateTime endOn)
        {
            var result = await _db.GetAvailableSpacesByTypeAsync(spaceTypeId, startOn, endOn);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetAvailabilityCountsAsync()
        {
            var result = await _db.GetAvailabilityCountsAsync();
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetSpaceSummaryAsync(int id)
        {
            var space = await _db.GetSpaceSummaryAsync(id);
            if (space is null) return ApiResponse.Fail("Space not found");
            return ApiResponse.Ok(space);
        }

        public async Task<ApiResponse> CreateSpaceAsync(SpaceInsertRequest request, int? actorId)
        {
            var (id, publicId) = await _db.InsertSpaceAsync(
                request.Name, request.LocationId, request.SpaceTypeId,
                request.Code, request.Description, request.FloorId,
                request.ImageUrl, request.Capacity, actorId);
            return ApiResponse.Ok(new { id, publicId }, "Space created.");
        }

        public async Task<ApiResponse> UpdateSpaceAsync(int id, SpaceUpdateRequest request, int? actorId)
        {
            await _db.UpdateSpaceAsync(id, request.Name, request.LocationId, request.SpaceTypeId,
                request.Code, request.Description, request.FloorId,
                request.ImageUrl, request.Capacity, actorId);
            return ApiResponse.Ok("Space updated.");
        }

        public async Task<ApiResponse> DeleteSpaceAsync(int id)
        {
            try
            {
                await _db.DeleteSpaceAsync(id);
                return ApiResponse.Ok("Space deleted.");
            }
            catch (Exception ex)
            {
                return ApiResponse.Fail(ex.Message);
            }
        }
    }
}
