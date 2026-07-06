using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Booking lifecycle service.
    /// Mirrors all Python booking endpoints in main.py exactly,
    /// including auto-assignment and smart booking logic.
    /// </summary>
    public class BookingService : IBookingService
    {
        private readonly IDbRepository _db;

        public BookingService(IDbRepository db) => _db = db;

        public async Task<IEnumerable<object>> GetAllBookingsAsync() =>
            (await _db.GetAllBookingsAsync()).Cast<object>();

        public async Task<IEnumerable<object>> GetMyBookingsAsync(string userEmail)
        {
            var (userId, _) = await _db.GetUserIdByEmailAsync(userEmail);
            if (userId is null) return [];
            return (await _db.GetMyBookingsAsync(userId.Value)).Cast<object>();
        }

        public async Task<ApiResponse> GetBookingByIdAsync(string id, string userEmail)
        {
            var (userId, _) = await _db.GetUserIdByEmailAsync(userEmail);
            if (userId is null) return ApiResponse.Fail("User not found");

            var numericId = await _db.GetBookingNumericIdByGuidAsync(id);
            if (numericId is null) return ApiResponse.Fail("Booking not found");

            var data = await _db.GetBookingByIdAsync(userId.Value, numericId.Value);
            if (data is null) return ApiResponse.Fail("Booking not found");
            return ApiResponse.Ok(data);
        }

        public async Task<ApiResponse> GetBookingCalendarAsync(int spaceId, int year, int month)
        {
            var result = await _db.GetBookingCalendarAsync(spaceId, year, month);
            var bookedDates = new HashSet<string>();

            foreach (var booking in result)
            {
                var startStr = booking.TryGetValue("startDate", out var s) ? s?.ToString() : null;
                var endStr   = booking.TryGetValue("endDate", out var e) ? e?.ToString() : null;
                if (startStr is null || endStr is null) continue;

                var start = DateTime.Parse(startStr);
                var end   = DateTime.Parse(endStr);
                for (var d = start; d <= end; d = d.AddDays(1))
                    bookedDates.Add(d.ToString("yyyy-MM-dd"));
            }

            return ApiResponse.Ok(new { bookedDates, bookings = result });
        }

        /// <summary>
        /// Creates a booking — auto-assignment if spaceId is absent/auto,
        /// otherwise manual booking. Mirrors Python make_booking() exactly.
        /// </summary>
        public async Task<ApiResponse> CreateBookingAsync(BookingRequest request, string userEmail)
        {
            var spaceIdStr = request.SpaceId?.ToString();
            var isAuto     = string.IsNullOrWhiteSpace(spaceIdStr)
                          || spaceIdStr == "auto"
                          || (request.SpaceType is not null && string.IsNullOrWhiteSpace(spaceIdStr));

            double amount  = request.TotalAmount ?? 0;
            string? method = null, refNum = null;
            if (request.Payment is not null)
            {
                amount = request.Payment.Amount;
                method = request.Payment.Method;
                refNum = request.Payment.ReferenceNumber ?? request.Payment.BankDepositId ?? "";
            }

            if (isAuto)
            {
                var spaceType = request.SpaceType
                    ?? (request.SpaceId is string s ? s : "Private Office");

                var result = await _db.CreateBookingWithAutoAssignmentAsync(
                    userEmail, spaceType, request.StartDateTime, request.EndDateTime,
                    request.Notes ?? "", amount, method, refNum);

                return ApiResponse.Ok(new
                {
                    id                 = result.TryGetValue("idGuid", out var g) ? g : result.TryGetValue("id", out var i) ? i : null,
                    assignedSpaceId    = result.TryGetValue("assignedSpaceId", out var asi) ? asi : null,
                    assignedSpaceName  = result.TryGetValue("assignedSpaceName", out var asn) ? asn : null,
                    spaceType          = result.TryGetValue("spaceType", out var st) ? st : null,
                    totalAmount        = amount,
                    isAutoAssigned     = true,
                }, "Booking successful with auto-assigned space.");
            }

            // Manual booking
            var (userId, _) = await _db.GetUserIdByEmailAsync(userEmail);
            if (userId is null) return ApiResponse.Fail("User not found");

            // Resolve GUID spaceId to numeric
            object spaceId = request.SpaceId!;
            if (GuidHelper.IsGuid(spaceIdStr))
            {
                var numericSpaceId = await _db.GetSpaceNumericIdByGuidAsync(spaceIdStr!);
                if (numericSpaceId is null) return ApiResponse.Fail("Space not found");
                spaceId = numericSpaceId.Value;
            }

            var booking = await _db.CreateBookingAsync(userId.Value, spaceId,
                request.StartDateTime, request.EndDateTime,
                request.Notes ?? "", amount, method, refNum);

            return ApiResponse.Ok(new
            {
                id          = booking.TryGetValue("idGuid", out var bg) ? bg : booking.TryGetValue("id", out var bi) ? bi : null,
                spaceId     = request.SpaceId,
                totalAmount = amount,
            }, "Booking successful.");
        }

        public async Task<ApiResponse> CreateSmartBookingAsync(SmartBookingRequest request, string userEmail)
        {
            var result = await _db.CreateSmartBookingAsync(
                userEmail, request.SpaceCategory, request.StartDateTime, request.EndDateTime,
                request.Notes ?? "", request.TotalAmount ?? 0,
                request.PaymentMethod, request.PaymentRef, request.Capacity);

            return ApiResponse.Ok(new
            {
                success           = true,
                id                = result.TryGetValue("id", out var id) ? id : null,
                bookingId         = result.TryGetValue("idGuid", out var g) ? g : result.TryGetValue("id", out var i) ? i : null,
                assignedSpace     = result.TryGetValue("assignedSpaceCode", out var asc) ? asc : null,
                assignedSpaceName = result.TryGetValue("assignedSpaceName", out var asn) ? asn : null,
                assignedSpaceId   = result.TryGetValue("assignedSpaceId", out var asi) ? asi : null,
                spaceCategory     = result.TryGetValue("spaceCategory", out var sc) ? sc : null,
                totalAmount       = request.TotalAmount,
            }, "Booking created with auto-assigned space.");
        }

        public async Task<ApiResponse> CancelBookingAsync(string id, string userEmail)
        {
            var (userId, _) = await _db.GetUserIdByEmailAsync(userEmail);
            if (userId is null) return ApiResponse.Fail("User not found");

            var numericId = await _db.GetBookingNumericIdByGuidAsync(id);
            if (numericId is null) return ApiResponse.Fail("Booking not found");

            await _db.CancelBookingAsync(userId.Value, numericId.Value);
            return ApiResponse.Ok("Booking cancelled.");
        }

        public async Task<ApiResponse> UpdateBookingStatusAsync(string id, string status)
        {
            var statusVal = AppConstants.BookingStatus.Map.TryGetValue(status, out var v) ? v : 1;
            await _db.UpdateBookingStatusAsync(id, statusVal);
            return ApiResponse.Ok("Booking status updated.");
        }

        public async Task<ApiResponse> UpdateBookingAsync(string id, BookingRequest request)
        {
            await _db.UpdateBookingDatesAsync(id, request.StartDateTime, request.EndDateTime);
            return ApiResponse.Ok("Booking updated.");
        }

        public async Task<ApiResponse> ReassignBookingAsync(string id, ReassignBookingRequest request, string adminEmail)
        {
            var numericId = await _db.GetBookingNumericIdByGuidAsync(id);
            if (numericId is null) return ApiResponse.Fail("Booking not found");

            var result = await _db.ReassignBookingAsync(numericId.Value, request.SpaceId, adminEmail);
            return ApiResponse.Ok(result, "Booking reassigned successfully.");
        }

        public async Task<ApiResponse> GetAvailableSpacesAsync(string spaceType, string start, string end)
        {
            var result = await _db.GetAvailableSpacesAsync(spaceType, start, end);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetAvailableSpacesForReassignmentAsync(
            string spaceType, string start, string end, int? excludeBookingId)
        {
            var result = await _db.GetAvailableSpacesForReassignmentAsync(spaceType, start, end, excludeBookingId);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetSmartAvailableSpacesAsync(
            string spaceCategory, string start, string end, int? capacity)
        {
            var result = await _db.GetAvailableSpacesV2Async(spaceCategory, start, end, capacity);
            return ApiResponse.Ok(result);
        }
    }
}
