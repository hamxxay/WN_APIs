using WorkNest.Application.DTOs.Space;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Space management service.
    /// Mirrors all Python space endpoints in main.py exactly.
    /// </summary>
    public class SpaceService : ISpaceService
    {
        private readonly IDbRepository _db;

        public SpaceService(IDbRepository db) => _db = db;

        public async Task<IEnumerable<object>> GetAllSpacesAsync() =>
            (await _db.GetAllSpacesAsync()).Cast<object>();

        public async Task<IEnumerable<object>> GetAvailableSpacesAsync()
        {
            var spaces = await _db.GetAllSpacesAsync();
            return spaces.Where(s =>
                (s.TryGetValue("spaceStatus", out var st) && st?.ToString() == "Available") ||
                (s.TryGetValue("status", out var s2) && Convert.ToString(s2) == "1"))
                .Cast<object>();
        }

        public async Task<ApiResponse> GetAvailableSpacesByTypeAsync(string spaceType, string? start, string? end)
        {
            var result = await _db.GetAvailableSpacesByTypeAsync(spaceType, start, end);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetAvailabilityCountsAsync()
        {
            var result = await _db.GetAvailabilityCountsAsync();
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetSpaceSummaryAsync(string id)
        {
            var space        = await _db.GetSpaceSummaryAsync(id);
            if (space is null) return ApiResponse.Fail("Space not found");
            var reservations = (await _db.GetSpaceReservationsAsync(id)).ToList();

            var confirmed = reservations.Where(r =>
                r.TryGetValue("bookingStatus", out var s) && s?.ToString() == "Confirmed").ToList();
            var cancelled = reservations.Where(r =>
                r.TryGetValue("bookingStatus", out var s) && s?.ToString() == "Cancelled").ToList();

            return ApiResponse.Ok(new
            {
                space = new
                {
                    id            = space.TryGetValue("idGUID", out var g) ? g?.ToString() : null,
                    name          = space.TryGetValue("name", out var n) ? n?.ToString() : null,
                    code          = space.TryGetValue("code", out var c) ? c?.ToString() : null,
                    locationName  = space.TryGetValue("locationName", out var l) ? l?.ToString() : null,
                    spaceTypeName = space.TryGetValue("spaceTypeName", out var t) ? t?.ToString() : null,
                    status        = space.TryGetValue("status", out var st) ? st?.ToString() : null,
                },
                stats = new
                {
                    totalBookings     = reservations.Count,
                    totalReservedDays = reservations.Sum(r => r.TryGetValue("reservedDays", out var d) ? Convert.ToInt32(d) : 0),
                    confirmedBookings = confirmed.Count,
                    cancelledBookings = cancelled.Count,
                    collectedRevenue  = confirmed.Sum(r => r.TryGetValue("totalAmount", out var a) ? Convert.ToDouble(a) : 0),
                },
                recentReservations = reservations.Take(10),
            });
        }

        public async Task<ApiResponse> CreateSpaceAsync(SpaceInsertRequest request)
        {
            var newId = await _db.InsertSpaceAsync(request.Name, request.LocationId, request.SpaceTypeId,
                request.Code, request.Description, request.FloorId,
                request.PricePerDay, request.PricePerHour, request.ImageUrl, request.Amenities);
            return ApiResponse.Ok(new { id = newId }, "Space created.");
        }

        public async Task<ApiResponse> UpdateSpaceAsync(string id, SpaceUpdateRequest request)
        {
            var numericId = await _db.GetSpaceNumericIdByGuidAsync(id);
            if (numericId is null) return ApiResponse.Fail("Space not found");

            await _db.UpdateSpaceAsync(numericId.Value, request.Name, request.LocationId,
                request.SpaceTypeId, request.Code, request.Description, request.FloorId,
                request.PricePerDay, request.PricePerHour, request.ImageUrl, request.Amenities);
            return ApiResponse.Ok("Space updated.");
        }

        public async Task<ApiResponse> DeleteSpaceAsync(string id)
        {
            await _db.SoftDeleteSpaceAsync(id);
            return ApiResponse.Ok("Space deleted.");
        }
    }
}
