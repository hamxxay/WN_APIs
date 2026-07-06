using WorkNest.Application.DTOs.Floor;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class FloorService : IFloorService
    {
        private readonly IDbRepository _db;
        public FloorService(IDbRepository db) => _db = db;

        public async Task<ApiResponse> GetAllFloorsAsync(int? locationId)
        {
            var rows = await _db.GetAllFloorsAsync(locationId);
            return ApiResponse.Ok(rows);
        }

        public async Task<ApiResponse> CreateFloorAsync(FloorUpsertRequest request)
        {
            var id = await _db.CreateFloorAsync(request.LocationId, request.FloorName);
            return ApiResponse.Ok(new { id }, "Floor created.");
        }
    }
}
