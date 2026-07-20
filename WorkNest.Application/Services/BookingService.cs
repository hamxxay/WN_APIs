using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;
using WorkNest.Common.Responses;
using WorkNest.Application.DTOs.AccountCoa;

namespace WorkNest.Application.Services
{
    public class BookingService : IBookingService
    {
        private readonly IDbRepository _db;

        public BookingService(IDbRepository db) => _db = db;

        public async Task<IEnumerable<object>> GetAllBookingsAsync() =>
            (await _db.GetAllBookingsAsync()).Cast<object>();

        public async Task<IEnumerable<object>> GetMyBookingsAsync(string userEmail)
        {
            var (_, userGuid) = await _db.GetUserIdByEmailAsync(userEmail);
            if (userGuid is null) return [];
            return (await _db.GetMyBookingsAsync(userGuid)).Cast<object>();
        }

        public async Task<ApiResponse> GetBookingByIdAsync(string id, string userEmail)
        {
            var data = await _db.GetBookingByGuidAsync(id);
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
                var endStr   = booking.TryGetValue("endDate",   out var e) ? e?.ToString() : null;
                if (startStr is null || endStr is null) continue;

                var start = DateTime.Parse(startStr);
                var end   = DateTime.Parse(endStr);
                for (var d = start; d <= end; d = d.AddDays(1))
                    bookedDates.Add(d.ToString("yyyy-MM-dd"));
            }

            return ApiResponse.Ok(new { bookedDates, bookings = result });
        }

        public async Task<ApiResponse> CreateAdminBookingAsync(AdminBookingRequest request)
        {
            var email = request.CustomerEmail;
            if (string.IsNullOrWhiteSpace(email))
                return ApiResponse.Fail("CustomerEmail is required to create a booking.");

            var nameParts = (request.CustomerName ?? email).Split(' ', 2);
            var (_, userId) = await _db.SyncUserAsync(
                email,
                nameParts[0],
                nameParts.Length > 1 ? nameParts[1] : "",
                request.Phone);

            if (userId is null)
                return ApiResponse.Fail("Failed to resolve user for booking.");

            var spaceRow = await _db.GetSpaceSummaryAsync(request.SpaceId);
            var spaceTypeName = spaceRow?.TryGetValue("SpaceTypeName", out var stn) == true ? stn?.ToString()
                              : spaceRow?.TryGetValue("spaceTypeName", out var stn2) == true ? stn2?.ToString()
                              : null;
            var securityDeposit = spaceTypeName is not null
                ? await _db.GetSecurityDepositAsync(spaceTypeName)
                : 0;

            var totalAmount = (request.TotalAmount ?? 0) + securityDeposit;

            var booking = await _db.CreateBookingAsync(
                userId,
                request.SpaceId,
                request.StartDateTime,
                request.EndDateTime,
                request.Notes ?? "Admin Booking",
                totalAmount,
                "Cash",
                null,
                request.CustomerCode);

            return ApiResponse.Ok(new
            {
                id              = booking.TryGetValue("idGUID", out var g) ? g
                                : booking.TryGetValue("NewId",  out var n) ? n
                                : booking.TryGetValue("id",     out var i) ? i : null,
                spaceId         = request.SpaceId,
                rentAccountId   = booking.TryGetValue("RentAccountId",    out var ra) ? ra : null,
                depositAccountId= booking.TryGetValue("DepositAccountId", out var da) ? da : null,
                bookingAmount   = request.TotalAmount ?? 0,
                securityDeposit = securityDeposit,
                totalAmount     = totalAmount,
            }, "Admin booking created successfully.");
        }

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
                    id                = result.TryGetValue("idGUID", out var g) ? g : result.TryGetValue("id", out var i) ? i : null,
                    assignedSpaceId   = result.TryGetValue("assignedSpaceId",   out var asi) ? asi : null,
                    assignedSpaceName = result.TryGetValue("assignedSpaceName", out var asn) ? asn : null,
                    spaceType         = result.TryGetValue("spaceType",         out var st)  ? st  : null,
                    totalAmount       = amount,
                    isAutoAssigned    = true,
                }, "Booking successful with auto-assigned space.");
            }

            // Manual booking — resolve user GUID then book by space GUID
            var (_, userGuid) = await _db.GetUserIdByEmailAsync(userEmail);
            if (userGuid is null) return ApiResponse.Fail("User not found");

            var spaceGuid = spaceIdStr!;
            var booking = await _db.CreateBookingAsync(userGuid, spaceGuid,
                request.StartDateTime, request.EndDateTime,
                request.Notes ?? "", amount, method, refNum);

            return ApiResponse.Ok(new
            {
                id          = booking.TryGetValue("idGUID", out var bg) ? bg : booking.TryGetValue("id", out var bi) ? bi : null,
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
                id                = result.TryGetValue("id",               out var id)  ? id  : null,
                bookingId         = result.TryGetValue("idGUID",           out var g)   ? g   : result.TryGetValue("id", out var i) ? i : null,
                assignedSpace     = result.TryGetValue("assignedSpaceCode", out var asc) ? asc : null,
                assignedSpaceName = result.TryGetValue("assignedSpaceName", out var asn) ? asn : null,
                assignedSpaceId   = result.TryGetValue("assignedSpaceId",   out var asi) ? asi : null,
                spaceCategory     = result.TryGetValue("spaceCategory",     out var sc)  ? sc  : null,
                totalAmount       = request.TotalAmount,
            }, "Booking created with auto-assigned space.");
        }

        public async Task<ApiResponse> GetBookingAccountAsync(string bookingGuid)
        {
            var booking = await _db.GetBookingByGuidAsync(bookingGuid);
            if (booking is null) return ApiResponse.Fail("Booking not found.");

            if (!booking.TryGetValue("AccountId", out var rawId) || rawId is null)
                return ApiResponse.Fail("No bank account linked to this booking.");

            var accountId = Convert.ToInt32(rawId);
            var account   = await _db.GetAccountCoaByIdAsync(accountId);
            if (account is null) return ApiResponse.Fail("Linked bank account not found.");

            return ApiResponse.Ok(new AccountCoaDto
            {
                AccountId   = accountId,
                Description = account.TryGetValue("Description", out var d) && d is not null ? d.ToString()! : string.Empty,
            });
        }

        public async Task<ApiResponse> CancelBookingAsync(string id, string userEmail)
        {
            var (_, userGuid) = await _db.GetUserIdByEmailAsync(userEmail);
            if (userGuid is null) return ApiResponse.Fail("User not found");

            await _db.CancelBookingAsync(userGuid, id);
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
            var result = await _db.ReassignBookingAsync(id, request.SpaceId, adminEmail);
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
