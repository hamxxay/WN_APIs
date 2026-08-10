using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class BookingService : IBookingService
    {
        private readonly IDbRepository _db;
        public BookingService(IDbRepository db) => _db = db;

        public async Task<(IEnumerable<object> Items, int Total)> GetBookingsAsync(int page, int limit, string? search)
        {
            var (rows, total) = await _db.GetBookingsAsync(page, limit, search);
            return (rows.Cast<object>(), total);
        }

        public async Task<IEnumerable<object>> GetMyBookingsAsync(string userEmail) =>
            (await _db.GetMyBookingsAsync(userEmail)).Cast<object>();

        public async Task<IEnumerable<object>> GetRecentBookingsAsync(int top = 10) =>
            (await _db.GetRecentBookingsAsync(top)).Cast<object>();

        public async Task<ApiResponse> GetBookingByIdAsync(Guid publicId, string? userEmail)
        {
            var data = await _db.GetBookingByPublicIdAsync(publicId, userEmail);
            if (data is null) return ApiResponse.Fail("Booking not found");
            return ApiResponse.Ok(data);
        }

        public async Task<ApiResponse> GetBookingCalendarAsync(int spaceId, int year, int month)
        {
            var result = await _db.GetBookingCalendarAsync(spaceId, year, month);
            var bookedDates = new HashSet<string>();
            foreach (var b in result)
            {
                var s = b.TryGetValue("StartOn", out var sv) ? sv as DateTime? : null;
                var e = b.TryGetValue("EndOn",   out var ev) ? ev as DateTime? : null;
                if (s is null || e is null) continue;
                for (var d = s.Value.Date; d <= e.Value.Date; d = d.AddDays(1))
                    bookedDates.Add(d.ToString("yyyy-MM-dd"));
            }
            return ApiResponse.Ok(new { bookedDates, bookings = result });
        }

        public async Task<ApiResponse> GetAvailableSpacesForBookingAsync(int spaceTypeId, DateTime startOn, DateTime endOn, int? capacity)
        {
            var result = await _db.GetAvailableSpacesForBookingAsync(spaceTypeId, startOn, endOn, capacity);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetAvailableSpacesForReassignmentAsync(int spaceTypeId, DateTime startOn, DateTime endOn, int excludeBookingId)
        {
            var result = await _db.GetAvailableSpacesForReassignmentAsync(spaceTypeId, startOn, endOn, excludeBookingId);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetSmartAvailableSpacesAsync(string categoryCode, DateTime startOn, DateTime endOn, int? capacity)
        {
            var result = await _db.GetSmartAvailableSpacesAsync(categoryCode, startOn, endOn, capacity);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> CreateBookingAsync(BookingRequest request, string userEmail)
        {
            var userRow = await _db.GetUserByEmailAsync(userEmail);
            if (userRow is null) return ApiResponse.Fail("User not found");
            var userId = userRow.TryGetValue("Id", out var uid) ? Convert.ToInt32(uid) : (int?)null;
            if (userId is null) return ApiResponse.Fail("User ID not resolved");

            var pricingId = await ResolvePricingIdAsync(request.SpaceId);
            if (pricingId == 0) return ApiResponse.Fail("No active pricing found for this space.");

            var result = await _db.InsertBookingAsync(
                userId.Value, request.SpaceId, pricingId,
                request.StartDateTime, request.EndDateTime,
                request.Notes, userId, userEmail);

            return ApiResponse.Ok(result, "Booking created.");
        }

        public async Task<ApiResponse> CreateAdminBookingAsync(AdminBookingRequest request, string? actorEmail)
        {
            int userId = request.UserId;

            // Resolve userId from GUID if int not provided
            if (userId == 0 && !string.IsNullOrWhiteSpace(request.UserIdGuid)
                && Guid.TryParse(request.UserIdGuid, out var userGuid))
            {
                var userRow = await _db.GetUserByPublicIdAsync(userGuid);
                if (userRow is not null)
                    userId = userRow.TryGetValue("Id", out var uid) ? Convert.ToInt32(uid) : 0;
            }

            if (userId == 0 && !string.IsNullOrWhiteSpace(request.CustomerEmail))
            {
                var (id, _) = await _db.SyncUserAsync(request.CustomerEmail, request.CustomerName, request.Phone);
                if (id is null) return ApiResponse.Fail("Failed to resolve user.");
                userId = id.Value;
            }

            int spaceId = request.SpaceId;

            // Resolve spaceId from GUID if int not provided
            if (spaceId == 0 && !string.IsNullOrWhiteSpace(request.SpaceIdGuid))
            {
                var (rows, _) = await _db.GetSpacesAsync(1, 10000, null);
                var match = rows.FirstOrDefault(r =>
                    r.TryGetValue("PublicId", out var g) &&
                    string.Equals(g?.ToString(), request.SpaceIdGuid, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    spaceId = match.TryGetValue("Id", out var sid) ? Convert.ToInt32(sid) : 0;
            }

            if (spaceId == 0)
                return ApiResponse.Fail("SpaceId is required.");

            int? actorId = null;
            if (!string.IsNullOrWhiteSpace(actorEmail))
            {
                var actorRow = await _db.GetUserByEmailAsync(actorEmail);
                actorId = actorRow?.TryGetValue("Id", out var aid) == true ? Convert.ToInt32(aid) : (int?)null;
            }

            var pricingId = await ResolvePricingIdAsync(spaceId);
            if (pricingId == 0) return ApiResponse.Fail("No active pricing found for this space.");

            var result = await _db.InsertBookingAsync(
                userId, spaceId, pricingId,
                request.StartDateTime, request.EndDateTime,
                request.Notes, actorId, request.CustomerEmail);

            return ApiResponse.Ok(result, "Admin booking created.");
        }

        public async Task<ApiResponse> CreateSmartBookingAsync(SmartBookingRequest request, string userEmail)
        {
            var userRow = await _db.GetUserByEmailAsync(userEmail);
            if (userRow is null) return ApiResponse.Fail("User not found");
            var actorId = userRow.TryGetValue("Id", out var uid) ? Convert.ToInt32(uid) : (int?)null;

            var result = await _db.InsertSmartBookingAsync(
                userEmail, request.CategoryCode,
                request.StartDateTime, request.EndDateTime,
                request.Capacity, request.Notes, actorId);

            return ApiResponse.Ok(result, "Smart booking created.");
        }

        public async Task<ApiResponse> UpdateBookingAsync(int id, BookingUpdateRequest request, int? actorId)
        {
            await _db.UpdateBookingAsync(id, request.StartDateTime, request.EndDateTime, request.Notes, actorId);
            return ApiResponse.Ok("Booking updated.");
        }

        public async Task<ApiResponse> UpdateBookingStatusAsync(int id, byte statusId, int? actorId)
        {
            await _db.UpdateBookingStatusAsync(id, statusId, actorId);
            return ApiResponse.Ok("Booking status updated.");
        }

        public async Task<ApiResponse> CancelBookingAsync(int id, string userEmail, string? cancelReason)
        {
            var userRow = await _db.GetUserByEmailAsync(userEmail);
            var actorId = userRow?.TryGetValue("Id", out var uid) == true ? Convert.ToInt32(uid) : (int?)null;
            await _db.CancelBookingAsync(id, userEmail, cancelReason, actorId);
            return ApiResponse.Ok("Booking cancelled.");
        }

        public async Task<ApiResponse> ReassignBookingAsync(int id, ReassignBookingRequest request, string userEmail)
        {
            var userRow = await _db.GetUserByEmailAsync(userEmail);
            var actorId = userRow?.TryGetValue("Id", out var uid) == true ? Convert.ToInt32(uid) : (int?)null;
            var newPricingId = await ResolvePricingIdAsync(request.NewSpaceId);
            await _db.ReassignBookingAsync(id, request.NewSpaceId, newPricingId, userEmail, actorId);
            return ApiResponse.Ok("Booking reassigned.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task<int> ResolvePricingIdAsync(int spaceId)
        {
            var pricing = await _db.GetActivePricingForSpaceAsync(spaceId);
            return pricing?.TryGetValue("PricingId", out var pid) == true && pid is not null
                ? Convert.ToInt32(pid) : 0;
        }
    }
}
