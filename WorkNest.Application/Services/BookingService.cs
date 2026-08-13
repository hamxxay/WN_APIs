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

            // Check if customer already exists for this user
            var customerRow = await _db.GetCustomerByUserIdAsync(userId.Value);
            if (customerRow is null)
            {
                // If details are not provided, return a validation fail prompting frontend
                if (string.IsNullOrWhiteSpace(request.FirstName))
                {
                    return new ApiResponse
                    {
                        IsSuccessful = false,
                        Message = "CustomerProfileRequired",
                        Errors = new List<string> { "Please complete your customer profile details to book a space." }
                    };
                }
            }

            var pricingId = await ResolvePricingIdAsync(request.SpaceId);
            if (pricingId == 0) return ApiResponse.Fail("No active pricing found for this space.");

            var result = await _db.InsertBookingAsync(
                userId.Value, request.SpaceId, pricingId,
                request.StartDateTime, request.EndDateTime,
                request.Notes, userId, userEmail,
                request.FirstName != null ? userEmail : null,
                request.FirstName, request.LastName, request.PhoneNumber,
                request.CnicOrPassport, request.Address, request.CityId,
                "Created during self-booking");

            if (result.TryGetValue("ErrorMessage", out var err) && err is not null && !string.IsNullOrWhiteSpace(err.ToString()))
            {
                return ApiResponse.Fail(err.ToString());
            }

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

            // Split customer name into first/last for the database record
            string? firstName = request.CustomerName;
            string? lastName = null;
            if (!string.IsNullOrWhiteSpace(request.CustomerName))
            {
                var parts = request.CustomerName.Split(' ', 2);
                firstName = parts[0];
                if (parts.Length > 1) lastName = parts[1];
            }

            var result = await _db.InsertBookingAsync(
                userId, spaceId, pricingId,
                request.StartDateTime, request.EndDateTime,
                request.Notes, actorId, request.CustomerEmail,
                request.CustomerEmail, firstName, lastName, request.Phone,
                null, null, null, "Created by administrator");

            if (result.TryGetValue("ErrorMessage", out var err) && err is not null && !string.IsNullOrWhiteSpace(err.ToString()))
            {
                return ApiResponse.Fail(err.ToString());
            }

            return ApiResponse.Ok(result, "Admin booking created.");
        }

        public async Task<ApiResponse> CreateSmartBookingAsync(SmartBookingRequest request, string userEmail)
        {
            var userRow = await _db.GetUserByEmailAsync(userEmail);
            if (userRow is null) return ApiResponse.Fail("User not found");
            var userId = userRow.TryGetValue("Id", out var uid) ? Convert.ToInt32(uid) : (int?)null;
            if (userId is null) return ApiResponse.Fail("User ID not resolved");

            // Check if customer already exists for this user
            var customerRow = await _db.GetCustomerByUserIdAsync(userId.Value);
            if (customerRow is null)
            {
                // If details are not provided, return a validation fail prompting frontend
                if (string.IsNullOrWhiteSpace(request.FirstName))
                {
                    return new ApiResponse
                    {
                        IsSuccessful = false,
                        Message = "CustomerProfileRequired",
                        Errors = new List<string> { "Please complete your customer profile details to book a space." }
                    };
                }
            }

            var result = await _db.InsertSmartBookingAsync(
                userEmail, request.CategoryCode,
                request.StartDateTime, request.EndDateTime,
                request.Capacity, request.Notes, userId,
                request.FirstName != null ? userEmail : null,
                request.FirstName, request.LastName, request.PhoneNumber,
                request.CnicOrPassport, request.Address, request.CityId,
                "Created during self-booking");

            if (result.TryGetValue("ErrorMessage", out var err) && err is not null && !string.IsNullOrWhiteSpace(err.ToString()))
            {
                return ApiResponse.Fail(err.ToString());
            }

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

        public async Task<ApiResponse> GetBookingDetailsAsync(string bookingIdentifier, string? userEmail)
        {
            var data = await _db.GetBookingDetailsAsync(bookingIdentifier, userEmail);
            return ApiResponse.Ok(data);
        }

        private static DateTime? ParseDateSafely(object? value)
        {
            if (value == null) return null;
            if (value is DateTime dt) return dt;
            if (value is DateOnly dOnly) return dOnly.ToDateTime(TimeOnly.MinValue);
            if (DateTime.TryParse(value.ToString(), out var parsed)) return parsed;
            return null;
        }

        public async Task<ApiResponse> GetChallanAsync(int bookingId)
        {
            var (header, lines) = await _db.GetChallanWithDetailsAsync(bookingId);
            if (header == null)
                return ApiResponse.Fail("Challan not found");

            var dto = new ChallanResponseDto
            {
                ChallanId = Convert.ToInt32(header["ChallanId"]),
                ChallanPublicId = header["ChallanPublicId"]?.ToString(),
                ChallanNumber = header["ChallanNumber"]?.ToString() ?? "",
                IssuedOn = ParseDateSafely(header["IssuedOn"]),
                ValidUntil = ParseDateSafely(header["ValidUntil"]),
                ChallanStatusId = Convert.ToInt32(header["ChallanStatusId"]),
                ChallanNotes = header["ChallanNotes"]?.ToString(),

                BookingId = Convert.ToInt32(header["BookingId"]),
                BookingPublicId = header["BookingPublicId"]?.ToString(),
                StartOn = ParseDateSafely(header["StartOn"]),
                EndOn = ParseDateSafely(header["EndOn"]),
                BookingStatusCode = header["BookingStatusCode"]?.ToString(),
                BookingStatusLabel = header["BookingStatusLabel"]?.ToString(),
                BookedOn = ParseDateSafely(header["BookedOn"]),

                CustomerName = header["CustomerName"]?.ToString(),
                CustomerEmail = header["CustomerEmail"]?.ToString(),

                SpaceCode = header["SpaceCode"]?.ToString(),
                SpaceName = header["SpaceName"]?.ToString(),
                SpaceCapacity = Convert.ToInt32(header["SpaceCapacity"]),
                SpaceTypeName = header["SpaceTypeName"]?.ToString(),

                LocationName = header["LocationName"]?.ToString(),
                BranchName = header["BranchName"]?.ToString(),
                CompanyName = header["CompanyName"]?.ToString(),

                BillingPeriodCode = header["BillingPeriodCode"]?.ToString(),
                BillingPeriodLabel = header["BillingPeriodLabel"]?.ToString(),
                SeatPrice = Convert.ToDecimal(header["SeatPrice"]),
                RoomPrice = Convert.ToDecimal(header["RoomPrice"]),
                SecurityDeposit = Convert.ToDecimal(header["SecurityDeposit"])
            };

            decimal total = 0;
            foreach (var line in lines)
            {
                var lineTotal = Convert.ToDecimal(line["LineTotal"]);
                total += lineTotal;

                dto.Details.Add(new ChallanLineDto
                {
                    LineId = Convert.ToInt32(line["LineId"]),
                    ChargeTypeCode = line["ChargeTypeCode"]?.ToString() ?? "",
                    ChargeTypeLabel = line["ChargeTypeLabel"]?.ToString() ?? "",
                    Description = line["Description"]?.ToString(),
                    Quantity = Convert.ToDecimal(line["Quantity"]),
                    UnitPrice = Convert.ToDecimal(line["UnitPrice"]),
                    DiscountAmount = Convert.ToDecimal(line["DiscountAmount"]),
                    TaxRate = Convert.ToDecimal(line["TaxRate"]),
                    TaxAmount = Convert.ToDecimal(line["TaxAmount"]),
                    LineTotal = lineTotal,
                    AccountId = line["AccountId"] != null ? Convert.ToInt32(line["AccountId"]) : (int?)null,
                    AccountName = line["AccountName"]?.ToString()
                });
            }

            dto.TotalPayable = total;

            return ApiResponse.Ok(dto);
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
