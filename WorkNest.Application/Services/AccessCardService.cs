using WorkNest.Application.DTOs.AccessCard;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class AccessCardService : IAccessCardService
    {
        private readonly IDbRepository _db;
        public AccessCardService(IDbRepository db) => _db = db;

        public async Task<ApiResponse> GetAllAccessCardsAsync(
            int page = 1,
            int limit = 20,
            string? search = null,
            int? bookingId = null,
            int? customerId = null,
            int? spaceId = null,
            int? status = null,
            IReadOnlyCollection<int>? locationIds = null)
        {
            var (rows, total) = locationIds == null
                ? await _db.GetAccessCardsAsync(page, limit, search, bookingId, customerId, spaceId, status)
                : await _db.GetAccessCardsInLocationsAsync(page, limit, search, bookingId, customerId, spaceId, status, locationIds);
            return ApiResponse.Ok(new { page, limit, total, data = rows });
        }

        public async Task<ApiResponse> GetAccessCardByIdAsync(string id)
        {
            var result = await _db.GetAccessCardByIdAsync(id);
            if (result is null) return ApiResponse.Fail("Access Card not found.");
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> CreateAccessCardAsync(AccessCardRequest request, int? createdById = null)
        {
            if (request.BookingId <= 0) return ApiResponse.Fail("BookingId is required.");
            if (request.CustomerId <= 0) return ApiResponse.Fail("CustomerId is required.");
            if (request.SpaceId <= 0) return ApiResponse.Fail("SpaceId is required.");
            if (request.LocationId <= 0) return ApiResponse.Fail("LocationId is required.");
            if (request.EndDate < request.StartDate) return ApiResponse.Fail("EndDate cannot be earlier than StartDate.");

            var result = await _db.CreateAccessCardAsync(
                request.LocationId,
                request.CustomerId,
                request.BookingId,
                request.SpaceId,
                request.CardNumber,
                request.StartDate,
                request.EndDate,
                request.Status,
                createdById);

            return ApiResponse.Ok(result, "Access Card created successfully.");
        }

        public async Task<ApiResponse> UpdateAccessCardAsync(string id, AccessCardRequest request, int? updatedById = null)
        {
            var existing = await _db.GetAccessCardByIdAsync(id);
            if (existing is null) return ApiResponse.Fail("Access Card not found.");

            await _db.UpdateAccessCardAsync(
                id,
                request.LocationId > 0 ? request.LocationId : null,
                request.CustomerId > 0 ? request.CustomerId : null,
                request.BookingId > 0 ? request.BookingId : null,
                request.SpaceId > 0 ? request.SpaceId : null,
                request.CardNumber,
                request.StartDate != default ? request.StartDate : null,
                request.EndDate != default ? request.EndDate : null,
                request.Status > 0 ? request.Status : null,
                updatedById);

            return ApiResponse.Ok("Access Card updated successfully.");
        }

        public async Task<ApiResponse> DeleteAccessCardAsync(string id)
        {
            var existing = await _db.GetAccessCardByIdAsync(id);
            if (existing is null) return ApiResponse.Fail("Access Card not found.");

            await _db.DeleteAccessCardAsync(id);
            return ApiResponse.Ok("Access Card deleted successfully.");
        }

        public async Task<(string? Error, int BookingId)> CheckCardPartiesAsync(AccessCardRequest request, string? existingId = null)
        {
            int bookingId = request.BookingId, customerId = request.CustomerId, spaceId = request.SpaceId;
            if (existingId != null)
            {
                var existing = await _db.GetAccessCardByIdAsync(existingId);
                if (existing is null) return (null, 0); // the update answers "not found" itself
                // Nothing about the card's booking / customer / space changes: nothing to check.
                if (bookingId <= 0 && customerId <= 0 && spaceId <= 0) return (null, 0);
                if (bookingId <= 0) bookingId = IntOf(existing, "BookingId");
                if (customerId <= 0) customerId = IntOf(existing, "CustomerId");
                if (spaceId <= 0) spaceId = IntOf(existing, "SpaceId");
            }
            if (bookingId <= 0) return (null, 0); // create: "BookingId is required." comes from CreateAccessCardAsync
            if (customerId > 0 && !await _db.IsCustomerOfBookingAsync(bookingId, customerId))
                return ("This customer is not the customer of this booking.", bookingId);
            if (spaceId > 0 && !await _db.IsSpaceOfBookingAsync(bookingId, spaceId))
                return ("This space is not part of this booking.", bookingId);
            return (null, bookingId);
        }

        private static int IntOf(IDictionary<string, object?> row, string key) =>
            row.TryGetValue(key, out var v) && v is not null && v is not DBNull && int.TryParse(v.ToString(), out var n) ? n : 0;

        public async Task<ApiResponse> GenerateAccessCardsForBookingAsync(int bookingId, int? createdById = null)
        {
            if (bookingId <= 0) return ApiResponse.Fail("Valid BookingId is required.");

            await _db.GenerateAccessCardsForBookingDbAsync(bookingId, createdById);
            var cards = await _db.GetAccessCardsAsync(1, 100, null, bookingId, null, null, null);
            return ApiResponse.Ok(cards.Rows, "Access cards generated for booking successfully.");
        }
    }
}
