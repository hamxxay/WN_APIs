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
            (await _db.GetAllSpacesAsync()).Select(r => (object)new
            {
                id            = r.TryGetValue("id",            out var i)   ? i   : null,
                idGuid        = r.TryGetValue("idGuid",        out var g)   ? g?.ToString()  : null,
                name          = r.TryGetValue("name",          out var n)   ? n?.ToString()  : null,
                code          = r.TryGetValue("code",          out var c)   ? c?.ToString()  : null,
                description   = r.TryGetValue("description",   out var d)   ? d?.ToString()  : null,
                floorId       = r.TryGetValue("floorId",       out var fi)  ? fi  : null,
                floorName     = r.TryGetValue("floorName",     out var fn)  ? fn?.ToString() : null,
                pricePerDay   = r.TryGetValue("pricePerDay",   out var ppd) ? ppd : null,
                pricePerHour  = r.TryGetValue("pricePerHour",  out var pph) ? pph : null,
                pricePerMonth = r.TryGetValue("pricePerMonth", out var ppm) ? ppm : null,
                imageUrl      = r.TryGetValue("imageUrl",      out var img) ? img?.ToString() : null,
                amenities     = r.TryGetValue("amenities",     out var am)  ? am?.ToString()  : null,
                status        = r.TryGetValue("status",        out var st)  ? st  : null,
                locationId    = r.TryGetValue("locationId",    out var li)  ? li  : null,
                locationIdGuid= r.TryGetValue("locationIdGuid",out var lig) ? lig?.ToString() : null,
                locationName  = r.TryGetValue("locationName",  out var ln)  ? ln?.ToString()  : null,
                spaceTypeId   = r.TryGetValue("spaceTypeId",   out var sti) ? sti : null,
                spaceTypeIdGuid=r.TryGetValue("spaceTypeIdGuid",out var stg)? stg?.ToString() : null,
                spaceTypeName = r.TryGetValue("spaceTypeName", out var stn) ? stn?.ToString() : null,
                capacity      = r.TryGetValue("capacity",      out var cap) ? cap : null,
            });

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
                request.PricePerDay, request.PricePerHour, request.PricePerMonth,
                request.ImageUrl, request.Amenities,
                request.RentAccountId, request.DepositAccountId);
            return ApiResponse.Ok(new { id = newId }, "Space created.");
        }

        public async Task<ApiResponse> UpdateSpaceAsync(string id, SpaceUpdateRequest request)
        {
            await _db.UpdateSpaceAsync(id, request.Name, request.LocationId,
                request.SpaceTypeId, request.Code, request.Description, request.FloorId,
                request.PricePerDay, request.PricePerHour, request.PricePerMonth,
                request.ImageUrl, request.Amenities,
                request.RentAccountId, request.DepositAccountId);
            return ApiResponse.Ok("Space updated.");
        }

        public async Task<ApiResponse> DeleteSpaceAsync(string id)
        {
            await _db.SoftDeleteSpaceAsync(id);
            return ApiResponse.Ok("Space deleted.");
        }
    }
}
