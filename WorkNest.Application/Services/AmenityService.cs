using WorkNest.Application.DTOs.Amenity;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class AmenityService : IAmenityService
    {
        private readonly IDbRepository _db;
        public AmenityService(IDbRepository db) => _db = db;

        public async Task<ApiResponse> GetAllAmenitiesAsync()
        {
            var rows = await _db.GetAllAmenitiesAsync();
            return ApiResponse.Ok(rows);
        }

        public async Task<ApiResponse> CreateAmenityAsync(AmenityUpsertRequest request)
        {
            var id = await _db.CreateAmenityAsync(request.Name);
            return ApiResponse.Ok(new { id }, "Amenity created.");
        }
    }
}
