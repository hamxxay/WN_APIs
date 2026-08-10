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

        public async Task<(IEnumerable<object> Items, int Total)> GetLocationsAsync(int page, int limit, string? search)
        {
            var (rows, total) = await _db.GetLocationsAsync(page, limit, search);
            return (rows.Cast<object>(), total);
        }

        public async Task<ApiResponse> CreateLocationAsync(LocationUpsertRequest request, int? actorId)
        {
            var (id, publicId) = await _db.InsertLocationAsync(
                request.BranchId, request.Name, request.Address, request.CityId,
                request.OpeningTime, request.ClosingTime,
                request.Latitude, request.Longitude, actorId);
            return ApiResponse.Ok(new { id, publicId }, "Location created.");
        }

        public async Task<ApiResponse> UpdateLocationAsync(int id, LocationUpdateRequest request)
        {
            await _db.UpdateLocationAsync(id, request.Name, request.Address, request.CityId,
                request.OpeningTime, request.ClosingTime, request.Latitude, request.Longitude);
            return ApiResponse.Ok("Location updated.");
        }

        public async Task<ApiResponse> DeleteLocationAsync(int id)
        {
            await _db.DeleteLocationAsync(id);
            return ApiResponse.Ok("Location deleted.");
        }
    }
}
