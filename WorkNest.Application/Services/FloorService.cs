using WorkNest.Application.DTOs.Floor;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class FloorService : IFloorService
    {
        private readonly IDbRepository _db;
        public FloorService(IDbRepository db) => _db = db;

        public async Task<ApiResponse> GetFloorsAsync(int? locationId)
        {
            var rows = await _db.GetFloorsAsync(locationId);
            return ApiResponse.Ok(rows);
        }

        public async Task<ApiResponse> CreateFloorAsync(FloorUpsertRequest request, int? actorId)
        {
            var id = await _db.InsertFloorAsync(request.LocationId, request.Name, request.FloorNumber, actorId);
            return ApiResponse.Ok(new { id }, "Floor created.");
        }
    }
}
