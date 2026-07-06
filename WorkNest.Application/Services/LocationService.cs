using WorkNest.Application.DTOs.Location;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class LocationService : ILocationService
    {
        private readonly IDbRepository _db;
        public LocationService(IDbRepository db) => _db = db;

        public async Task<IEnumerable<object>> GetAllLocationsAsync() =>
            (await _db.GetAllLocationsAsync()).Cast<object>();

        public async Task<ApiResponse> CreateLocationAsync(LocationUpsertRequest request)
        {
            var id = await _db.CreateLocationAsync(request.Name, request.Address, request.CityId,
                request.OpeningTime, request.ClosingTime, request.IsActive ?? true, request.BranchId);
            return ApiResponse.Ok(new { id }, "Location created.");
        }

        public async Task<ApiResponse> UpdateLocationAsync(string id, LocationUpsertRequest request)
        {
            await _db.UpdateLocationAsync(id, request.Name, request.Address, request.CityId,
                request.OpeningTime, request.ClosingTime, request.IsActive ?? true, request.BranchId);
            return ApiResponse.Ok("Location updated.");
        }

        public async Task<ApiResponse> DeleteLocationAsync(string id)
        {
            await _db.SoftDeleteLocationAsync(id);
            return ApiResponse.Ok("Location deleted.");
        }
    }
}
