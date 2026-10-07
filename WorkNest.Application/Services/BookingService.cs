using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.DTOs.Payment;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;
using WorkNest.Domain.Enums;

namespace WorkNest.Application.Services
{
    public class BookingService : IBookingService
    {
        private readonly IDbRepository _db;
        private readonly IEmailService _email;
        private readonly IPdfService _pdf;
        private readonly IOrderStatusService _orderStatus;
        private readonly IBusinessClock _clock;
        public BookingService(IDbRepository db, IEmailService email, IPdfService pdf, IOrderStatusService orderStatus, IBusinessClock clock) { _db = db; _email = email; _pdf = pdf; _orderStatus = orderStatus; _clock = clock; }

        // Booking statuses (WN_BookingStatuses): 5 Pending, 33 Confirmed. 1 is an older "pending" value still
        // written on new bookings (it has no WN_BookingStatuses row), so it counts as pending too.
        private static readonly int[] PendingBookingStatusIds = { 1, (int)BookingStatus.Pending };

        public async Task<List<int>> ConfirmPaidBookingsAsync()
        {
            var st = await _orderStatus.GetInvoiceStatusesAsync(); // Paid = legacy 2 + OrderStatus 'Paid'
            var voids = st.Cancelled.Append(5); // legacy void + OrderStatus 'Cancelled'
            var ids = await _db.GetPendingBookingsWithPaidFirstInvoiceDbAsync(PendingBookingStatusIds, st.Paid, voids);
            foreach (var id in ids)
                await _db.UpdateBookingStatusAsync(id, (byte)BookingStatus.Confirmed, null);
            return ids;
        }

        public async Task<(IEnumerable<object> Items, int Total)> GetBookingsAsync(int page, int limit, string? search, int? locationId = null)
        {
            var (rows, total) = await _db.GetBookingsAsync(page, limit, search, locationId);
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

        public async Task<ApiResponse> GetAvailableSpacesForBookingAsync(int spaceTypeId, DateTime startOn, DateTime endOn, int? capacity, string? shiftType = "24_7")
        {
            var result = await _db.GetAvailableSpacesForBookingAsync(spaceTypeId, startOn, endOn, capacity, shiftType);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetAvailableSpacesForReassignmentAsync(int spaceTypeId, DateTime startOn, DateTime endOn, int excludeBookingId, string? shiftType = "24_7")
        {
            var result = await _db.GetAvailableSpacesForReassignmentAsync(spaceTypeId, startOn, endOn, excludeBookingId, shiftType);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetSmartAvailableSpacesAsync(string categoryCode, DateTime startOn, DateTime endOn, int? capacity, string? shiftType = "24_7")
        {
            var result = await _db.GetSmartAvailableSpacesAsync(categoryCode, startOn, endOn, capacity, shiftType);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> CreateBookingAsync(BookingRequest request, string userEmail)
        {
            var userRow = await _db.GetUserByEmailAsync(userEmail);
            if (userRow is null) return ApiResponse.Fail("User not found");
            var userId = userRow.TryGetValue("Id", out var uid) ? Convert.ToInt32(uid) : (int?)null;
            if (userId is null) return ApiResponse.Fail("User ID not resolved");

            var customerRow = await _db.GetCustomerByUserIdAsync(userId.Value);
            if (customerRow is null)
            {
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

            var pricingId = await ResolvePricingIdAsync(request.SpaceId ?? 0);
            // pricingId may be 0; WN_Bookings_Insert handles category fallback

            var shiftType = !string.IsNullOrWhiteSpace(request.ShiftType) ? request.ShiftType : (!string.IsNullOrWhiteSpace(request.OfferingType) ? request.OfferingType : "24_7");

            var result = await _db.InsertBookingAsync(
                userId.Value, request.SpaceId ?? 0, pricingId,
                request.StartDateTime, request.EndDateTime,
                request.Notes, userId, userEmail,
                request.FirstName != null ? userEmail : null,
                request.FirstName, request.LastName, request.PhoneNumber,
                request.CnicOrPassport, request.Address, request.CityId,
                "Created during self-booking",
                0, "Percentage", 0, null, null, null, null, null, shiftType);

            if (result.TryGetValue("ErrorMessage", out var err) && err is not null && !string.IsNullOrWhiteSpace(err.ToString()))
                return ApiResponse.Fail(err.ToString() ?? "An error occurred");

            return ApiResponse.Ok(result, "Booking created.");
        }

        public async Task<ApiResponse> CreateAdminBookingAsync(AdminBookingRequest request, string? actorEmail, IReadOnlyCollection<int>? allowedLocationIds = null)
        {
            if (request is null)
                return ApiResponse.Fail("Request body is required.");

            int userId = request.UserId ?? 0;

            if (userId == 0 && !string.IsNullOrWhiteSpace(request.UserIdGuid))
            {
                if (int.TryParse(request.UserIdGuid, out var uIdInt) && uIdInt > 0)
                {
                    userId = uIdInt;
                }
                else if (Guid.TryParse(request.UserIdGuid, out var userGuid))
                {
                    var userRow = await _db.GetUserByPublicIdAsync(userGuid);
                    if (userRow is not null)
                        userId = userRow.TryGetValue("Id", out var uid) ? Convert.ToInt32(uid) : 0;
                }
            }

            if (userId == 0 && !string.IsNullOrWhiteSpace(request.CustomerEmail))
            {
                var (id, _) = await _db.SyncUserAsync(request.CustomerEmail, request.CustomerName, request.Phone);
                if (id is null) return ApiResponse.Fail("Failed to resolve or create customer user.");
                userId = id.Value;
            }

            if (userId == 0)
                return ApiResponse.Fail("UserId, UserIdGuid, or CustomerEmail is required.");

            int spaceId = request.SpaceId ?? 0;

            if (spaceId == 0 && !string.IsNullOrWhiteSpace(request.SpaceIdGuid))
            {
                if (int.TryParse(request.SpaceIdGuid, out var sIdInt) && sIdInt > 0)
                {
                    spaceId = sIdInt;
                }
                else
                {
                    var (rows, _) = await _db.GetSpacesAsync(1, 10000, null);
                    var match = rows.FirstOrDefault(r =>
                        (r.TryGetValue("PublicId", out var g) || r.TryGetValue("IdGUID", out g) || r.TryGetValue("publicId", out g) || r.TryGetValue("idGUID", out g) || r.TryGetValue("Id", out g) || r.TryGetValue("id", out g)) &&
                        string.Equals(g?.ToString(), request.SpaceIdGuid, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                        spaceId = match.TryGetValue("Id", out var sid) ? Convert.ToInt32(sid) : 0;
                }
            }

            if (spaceId == 0)
                return ApiResponse.Fail("SpaceId or SpaceIdGuid is required.");

            // Location-bound staff (Admin / Sales Executive) may only book spaces in the location they are working in.
            if (allowedLocationIds != null && await _db.GetSpaceLocationIdAsync(spaceId) is int spaceLocationId && !allowedLocationIds.Contains(spaceLocationId))
                return ApiResponse.Fail("You can only create bookings for the locations you're assigned to.");

            DateTime startOn = request.StartDateTime ?? ParseFlexibleDate(
                request.StartDate ?? request.StartOn ?? request.ContractStartDate ?? request.BillingStartDate ?? request.EffectiveFrom);

            DateTime endOn = request.EndDateTime ?? ParseFlexibleDate(
                request.EndDate ?? request.EndOn ?? request.ContractEndDate);

            if (startOn == default)
            {
                startOn = _clock.Today;
            }

            if (endOn == default || endOn <= startOn)
            {
                int months = request.BillingPeriodMonths ?? request.AdvanceRentMonths ?? 1;
                if (months <= 0) months = 1;
                endOn = startOn.AddMonths(months);
            }

            int? actorId = null;
            if (!string.IsNullOrWhiteSpace(actorEmail))
            {
                var actorRow = await _db.GetUserByEmailAsync(actorEmail);
                actorId = actorRow?.TryGetValue("Id", out var aid) == true ? Convert.ToInt32(aid) : (int?)null;
            }

            var pricingId = await ResolvePricingIdAsync(spaceId);

            var discountType = string.IsNullOrWhiteSpace(request.DiscountType) ? "Percentage" : request.DiscountType;
            var discountValue = request.DiscountValue > 0 ? request.DiscountValue : request.DiscountPercentage;
            if (discountType == "Percentage" && (discountValue < 0 || discountValue > 100))
                return ApiResponse.Fail("Percentage discount must be between 0 and 100.");
            if (discountValue < 0)
                return ApiResponse.Fail("Discount value cannot be negative.");

            // Base price and discount cap: same rules as QuotationService.CreateQuotationAsync.
            var (standardPerSeat, spaceCapacity, categoryCode, spaceName) = await GetSpacePricingAsync(spaceId);
            bool isPrivate = categoryCode is "PrivateOffice" or "Private";
            bool isMeetingRoom = categoryCode.Contains("Meeting", StringComparison.OrdinalIgnoreCase);
            // WN_Bookings_Insert prices a private office per seat (price x capacity). Meeting rooms also multiply
            // by @Capacity, so it is sent for private offices only.
            int? capacity = isPrivate ? (request.Capacity is > 0 ? request.Capacity : spaceCapacity) : null;
            int seats = Math.Max(1, capacity ?? 1);

            decimal? perSeatBasePrice = null;
            if (request.PerSeatBasePrice is > 0 && !isMeetingRoom)
            {
                if (standardPerSeat > 0 && request.PerSeatBasePrice.Value < standardPerSeat)
                    return ApiResponse.Fail($"Base price for {spaceName} is locked and can only be increased. Minimum allowed base price is PKR {standardPerSeat:N0}.");
                if (request.PerSeatBasePrice.Value > standardPerSeat)
                    perSeatBasePrice = request.PerSeatBasePrice.Value;
            }

            if (!isMeetingRoom && discountValue > 0)
            {
                var offeringTypes = (await _db.GetOfferingTypesAsync()).ToList();
                var offering = offeringTypes.FirstOrDefault(o => string.Equals(o["Description"]?.ToString(), request.OfferingType, StringComparison.OrdinalIgnoreCase))
                    ?? offeringTypes.FirstOrDefault();
                decimal baseCap = offering?["DiscountCap"] is { } dc ? Convert.ToDecimal(dc) : 10.00m;
                decimal perSeat = perSeatBasePrice ?? standardPerSeat;
                decimal monthlyRent = isPrivate ? perSeat * seats : perSeat;

                // The cap protects a floor per seat (standard rate minus the cap); a raised base price may be
                // discounted down to that floor and no further.
                decimal cap = baseCap;
                decimal maxFixed = Math.Round(monthlyRent * baseCap / 100m, 2);
                if (perSeatBasePrice.HasValue && standardPerSeat > 0)
                {
                    decimal floorPerSeat = standardPerSeat * (1 - baseCap / 100m);
                    cap = Math.Round((perSeat - floorPerSeat) / perSeat * 100m, 2, MidpointRounding.ToZero);
                    maxFixed = Math.Round((perSeat - floorPerSeat) * (isPrivate ? seats : 1), 2);
                }

                bool isPercentage = discountType is "Percentage" or "Percent";
                if (isPercentage && discountValue > cap)
                    return ApiResponse.Fail($"Discount ({discountValue}%) exceeds the maximum allowed discount of {cap}%.");
                if (!isPercentage && monthlyRent > 0 && discountValue > maxFixed)
                    return ApiResponse.Fail($"Discount (PKR {discountValue:N0}) exceeds the maximum allowed discount of PKR {maxFixed:N0} per month.");
            }

            string? firstName = request.CustomerName;
            string? lastName = null;
            if (!string.IsNullOrWhiteSpace(request.CustomerName))
            {
                var parts = request.CustomerName.Split(' ', 2);
                firstName = parts[0];
                if (parts.Length > 1) lastName = parts[1];
            }

            var shiftType = !string.IsNullOrWhiteSpace(request.ShiftType) ? request.ShiftType : (!string.IsNullOrWhiteSpace(request.OfferingType) ? request.OfferingType : "24_7");

            var result = await _db.InsertBookingAsync(
                userId, spaceId, pricingId,
                startOn, endOn,
                request.Notes, actorId, request.CustomerEmail,
                request.CustomerEmail, firstName, lastName, request.Phone,
                null, null, null, "Created by administrator",
                discountType == "Percentage" ? discountValue : 0,
                discountType, discountValue,
                request.SecurityDepositOverride, request.FloorId, request.BillingPeriodMonths, request.SecurityDepositMonths, request.AdvanceRentMonths,
                shiftType, capacity, perSeatBasePrice, request.SendWhtInvoice, request.SendWhtInvoice ? request.WhtRate : null);

            if (result.TryGetValue("ErrorMessage", out var err) && err is not null && !string.IsNullOrWhiteSpace(err.ToString()))
                return ApiResponse.Fail(err.ToString() ?? "An error occurred");

            return ApiResponse.Ok(result, "Admin booking created.");
        }

        /// <summary>Standard per-seat monthly price (s.Price, as WN_Bookings_Insert uses), capacity and category of a space.</summary>
        private async Task<(decimal StandardPerSeat, int Capacity, string CategoryCode, string Name)> GetSpacePricingAsync(int spaceId)
        {
            var (rows, _) = await _db.GetSpacesAsync(1, 10000, null);
            var row = rows.FirstOrDefault(r => r.TryGetValue("Id", out var id) && id is not null && Convert.ToInt32(id) == spaceId);
            if (row is null) return (0m, 1, "", "this space");
            decimal price = row.TryGetValue("Price", out var p) && p is not null ? Convert.ToDecimal(p) : 0m;
            int cap = row.TryGetValue("Capacity", out var c) && c is not null ? Convert.ToInt32(c) : 1;
            string category = row.TryGetValue("CategoryCode", out var cc) ? cc?.ToString() ?? "" : "";
            string name = row.TryGetValue("Name", out var n) ? n?.ToString() ?? "this space" : "this space";
            return (price, Math.Max(1, cap), category, name);
        }

        public async Task<ApiResponse> CreateSmartBookingAsync(SmartBookingRequest request, string userEmail)
        {
            var userRow = await _db.GetUserByEmailAsync(userEmail);
            if (userRow is null) return ApiResponse.Fail("User not found");
            var userId = userRow.TryGetValue("Id", out var uid) ? Convert.ToInt32(uid) : (int?)null;
            if (userId is null) return ApiResponse.Fail("User ID not resolved");

            var customerRow = await _db.GetCustomerByUserIdAsync(userId.Value);
            if (customerRow is null)
            {
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

            var smartShiftType = !string.IsNullOrWhiteSpace(request.ShiftType) ? request.ShiftType : (!string.IsNullOrWhiteSpace(request.OfferingType) ? request.OfferingType : "24_7");

            var result = await _db.InsertSmartBookingAsync(
                userEmail, request.CategoryCode,
                request.StartDateTime, request.EndDateTime,
                request.Capacity, request.Notes, userId,
                request.FirstName != null ? userEmail : null,
                request.FirstName, request.LastName, request.PhoneNumber,
                request.CnicOrPassport, request.Address, request.CityId,
                "Created during self-booking",
                smartShiftType);

            if (result.TryGetValue("ErrorMessage", out var err) && err is not null && !string.IsNullOrWhiteSpace(err.ToString()))
                return ApiResponse.Fail(err.ToString() ?? "An error occurred");

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
            var (header, lines) = await _db.GetBookingDetailsAsync(bookingIdentifier, userEmail);
            if (header == null)
            {
                return ApiResponse.Fail("Booking details not found");
            }

            var startOn = ParseDateSafely(header["StartOn"]);
            var endOn = ParseDateSafely(header["EndOn"]);
            var contractStart = ParseDateSafely(header.TryGetValue("ContractStartDate", out var csd) ? csd : header["StartOn"]);
            var contractEnd = ParseDateSafely(header.TryGetValue("ContractEndDate", out var ced) ? ced : header["EndOn"]);
            var spaceNum = header.TryGetValue("SpaceNumber", out var sn) && sn != null ? sn.ToString() : (header["SpaceCode"]?.ToString() ?? header["SpaceName"]?.ToString());
            var monthlyRent = header.TryGetValue("MonthlyRent", out var mr) && mr != null ? Convert.ToDecimal(mr) : 0m;
            var billingPeriodMonths = header.TryGetValue("BillingPeriodMonths", out var bpm) && bpm != null ? Convert.ToInt32(bpm) : 1;
            var billingPeriod = header.TryGetValue("BillingPeriod", out var bp) && bp != null ? bp.ToString() : (header.TryGetValue("BillingPeriodLabel", out var bpl) && bpl != null ? bpl.ToString() : $"{billingPeriodMonths} Month(s)");
            var numberOfMonths = header.TryGetValue("NumberOfMonths", out var nom) && nom != null ? Convert.ToInt32(nom) : 1;
            var currentCycleAmount = header.TryGetValue("CurrentCycleAmount", out var cca) && cca != null ? Convert.ToDecimal(cca) : 0m;
            var totalContractAmount = header.TryGetValue("TotalContractAmount", out var tca) && tca != null ? Convert.ToDecimal(tca) : 0m;

            if (currentCycleAmount <= 0m || (totalContractAmount > 0m && currentCycleAmount >= totalContractAmount && billingPeriodMonths < numberOfMonths))
            {
                if (monthlyRent > 0m && billingPeriodMonths > 0)
                {
                    currentCycleAmount = monthlyRent * billingPeriodMonths;
                }
            }

            var balanceLeft = header.TryGetValue("BalanceLeft", out var bl) && bl != null ? Convert.ToDecimal(bl) : 0m;
            var securityDeposit = header.TryGetValue("SecurityDeposit", out var sd) && sd != null ? Convert.ToDecimal(sd) : 0m;
            var totalPaidAmount = header.TryGetValue("TotalPaidAmount", out var tpa) && tpa != null ? Convert.ToDecimal(tpa) : 0m;
            var nextBillDueDate = ParseDateSafely(header.TryGetValue("NextBillDueDate", out var nbdd) ? nbdd : null);
            var nextBillingDate = ParseDateSafely(header.TryGetValue("NextBillingDate", out var nbd) ? nbd : nextBillDueDate);
            var calcInput = new ChallanCalculationInput
            {
                SpaceTypeName = header["SpaceTypeName"]?.ToString(),
                SpaceCode = header["SpaceCode"]?.ToString(),
                SpaceName = header["SpaceName"]?.ToString(),
                Capacity = header.TryGetValue("SpaceCapacity", out var scVal) && scVal != null ? Convert.ToInt32(scVal) : 1,
                StartOn = contractStart ?? startOn,
                EndOn = contractEnd ?? endOn,
                MonthlyRent = monthlyRent,
                CurrentCycleAmount = currentCycleAmount,
                TotalContractAmount = totalContractAmount,
                BillingPeriodMonths = billingPeriodMonths,
                ContractPeriodMonths = numberOfMonths,
                SecurityDeposit = securityDeposit,
                DiscountPercentage = header.TryGetValue("DiscountPercentage", out var dp) && dp != null ? Convert.ToDecimal(dp) : 0m,
                DiscountAmount = header.TryGetValue("DiscountAmount", out var da) && da != null ? Convert.ToDecimal(da) : 0m,
                AppliedTaxPercentage = header.TryGetValue("AppliedTaxPercentage", out var atp) && atp != null ? Convert.ToDecimal(atp) : 16.00m,
                PerSeatSupportRate = header.TryGetValue("PerSeatSupportRate", out var pssr) && pssr != null ? Convert.ToDecimal(pssr) : 2000.00m
            };
            var calcResult = ChallanCalculationService.Calculate(calcInput);

            decimal supportTaxOnCycle = calcResult.TaxAmount;
            decimal totalPayableInitial = calcResult.TotalPayable;
            securityDeposit = calcResult.SecurityDeposit;

            var contractObj = new ContractDetailsDto
            {
                ContractStartDate = contractStart ?? startOn,
                ContractEndDate = contractEnd ?? endOn,
                NumberOfMonths = numberOfMonths,
                MonthlyRent = monthlyRent,
                BillingPeriod = billingPeriod,
                BillingPeriodMonths = billingPeriodMonths,
                CurrentCycleAmount = currentCycleAmount,
                TotalContractAmount = totalContractAmount,
                NextBillingDate = nextBillingDate ?? nextBillDueDate,
                BalanceLeft = balanceLeft,
                SecurityDeposit = securityDeposit,
                SpaceNumber = spaceNum
            };

            var result = new BookingDetailsResponseDto
            {
                BookingId = Convert.ToInt32(header["BookingId"]),
                BookingPublicId = header["BookingPublicId"]?.ToString(),
                CustomerName = header["CustomerName"]?.ToString(),
                CustomerEmail = header["CustomerEmail"]?.ToString(),
                SpaceCode = header["SpaceCode"]?.ToString(),
                SpaceName = header["SpaceName"]?.ToString(),
                SpaceNumber = spaceNum,
                SpaceCapacity = header.TryGetValue("SpaceCapacity", out var sc) && sc != null ? Convert.ToInt32(sc) : 1,
                SpaceTypeName = header["SpaceTypeName"]?.ToString(),
                LocationName = header["LocationName"]?.ToString(),
                LocationAddress = header.TryGetValue("LocationAddress", out var la) && la != null ? la.ToString() : null,
                CityName = header.TryGetValue("CityName", out var cn) && cn != null ? cn.ToString() : null,
                CustomerAddress = header.TryGetValue("CustomerAddress", out var ca) && ca != null ? ca.ToString() : null,
                BranchName = header["BranchName"]?.ToString(),
                CompanyName = header["CompanyName"]?.ToString(),
                BookingStatusCode = header["BookingStatusCode"]?.ToString(),
                BookingStatusLabel = header["BookingStatusLabel"]?.ToString(),
                BookingStatus = header["BookingStatusLabel"]?.ToString() ?? header["BookingStatusCode"]?.ToString(),
                StartOn = startOn,
                EndOn = endOn,
                ContractStartDate = contractStart ?? startOn,
                ContractEndDate = contractEnd ?? endOn,
                NumberOfMonths = numberOfMonths,
                MonthlyRent = monthlyRent,
                BillingPeriod = billingPeriod,
                BillingPeriodLabel = header["BillingPeriodLabel"]?.ToString(),
                BillingPeriodMonths = billingPeriodMonths,
                CurrentCycleAmount = currentCycleAmount,
                FirstCycleRent = currentCycleAmount,
                TotalContractAmount = totalContractAmount,
                NextBillDueDate = nextBillDueDate,
                NextBillingDate = nextBillingDate ?? nextBillDueDate,
                BalanceLeft = balanceLeft,
                SecurityDeposit = securityDeposit,
                TotalAmount = header.TryGetValue("TotalAmount", out var ta) && ta != null ? Convert.ToDecimal(ta) : totalContractAmount,
                TotalPayable = totalPayableInitial,
                TotalPaidAmount = totalPaidAmount,
                BookedOn = ParseDateSafely(header["BookedOn"]),
                Contract = contractObj,
                Details = lines.Cast<object>().ToList()
            };

            return ApiResponse.Ok(result);
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

            var startOn = ParseDateSafely(header["StartOn"]);
            var endOn = ParseDateSafely(header["EndOn"]);
            var contractStart = ParseDateSafely(header.TryGetValue("ContractStartDate", out var csd) ? csd : header["StartOn"]);
            var contractEnd = ParseDateSafely(header.TryGetValue("ContractEndDate", out var ced) ? ced : header["EndOn"]);
            var spaceNum = header.TryGetValue("SpaceNumber", out var sn) && sn != null ? sn.ToString() : (header["SpaceCode"]?.ToString() ?? header["SpaceName"]?.ToString());
            var roomPrice = header.TryGetValue("RoomPrice", out var rp) && rp != null ? Convert.ToDecimal(rp) : 0m; var seatPrice = header.TryGetValue("SeatPrice", out var sp) && sp != null ? Convert.ToDecimal(sp) : 0m; var monthlyRent = header.TryGetValue("MonthlyRent", out var mr) && mr != null ? Convert.ToDecimal(mr) : (roomPrice > 0 ? roomPrice : seatPrice);
            var billingPeriodMonths = header.TryGetValue("BillingPeriodMonths", out var bpm) && bpm != null ? Convert.ToInt32(bpm) : 1;
            var numberOfMonths = header.TryGetValue("NumberOfMonths", out var nom) && nom != null ? Convert.ToInt32(nom) : 1;
            var currentCycleAmount = header.TryGetValue("CurrentCycleAmount", out var cca) && cca != null ? Convert.ToDecimal(cca) : 0m;
            var totalContractAmount = header.TryGetValue("TotalContractAmount", out var tca) && tca != null ? Convert.ToDecimal(tca) : 0m;
            var balanceLeft = header.TryGetValue("BalanceLeft", out var bl) && bl != null ? Convert.ToDecimal(bl) : 0m;
            var securityDeposit = header.TryGetValue("SecurityDeposit", out var sd) && sd != null ? Convert.ToDecimal(sd) : 0m;
            var totalPaidAmount = header.TryGetValue("TotalPaidAmount", out var tpa) && tpa != null ? Convert.ToDecimal(tpa) : 0m;
            var nextBillDueDate = ParseDateSafely(header.TryGetValue("NextBillDueDate", out var nbdd) ? nbdd : header["ValidUntil"]);
            var nextBillingDate = ParseDateSafely(header.TryGetValue("NextBillingDate", out var nbd) ? nbd : nextBillDueDate);
            var billingPeriod = header.TryGetValue("BillingPeriod", out var bp) && bp != null ? bp.ToString() : (header["BillingPeriodLabel"]?.ToString() ?? header["BillingPeriodCode"]?.ToString());

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
                StartOn = startOn,
                EndOn = endOn,
                ContractStartDate = contractStart ?? startOn,
                ContractEndDate = contractEnd ?? endOn,
                BookingStatusCode = header["BookingStatusCode"]?.ToString(),
                BookingStatusLabel = header["BookingStatusLabel"]?.ToString(),
                BookedOn = ParseDateSafely(header["BookedOn"]),

                CustomerName = header["CustomerName"]?.ToString(),
                CustomerEmail = header["CustomerEmail"]?.ToString(),

                SpaceCode = header["SpaceCode"]?.ToString(),
                SpaceNumber = spaceNum,
                SpaceName = header["SpaceName"]?.ToString(),
                SpaceCapacity = Convert.ToInt32(header["SpaceCapacity"]),
                SpaceTypeName = header["SpaceTypeName"]?.ToString(),

                LocationName = header["LocationName"]?.ToString(),
                LocationAddress = header.TryGetValue("LocationAddress", out var la2) && la2 != null ? la2.ToString() : null,
                CityName = header.TryGetValue("CityName", out var cn2) && cn2 != null ? cn2.ToString() : null,
                CustomerAddress = header.TryGetValue("CustomerAddress", out var ca2) && ca2 != null ? ca2.ToString() : null,
                BranchName = header["BranchName"]?.ToString(),
                CompanyName = header["CompanyName"]?.ToString(),

                BillingPeriodCode = header["BillingPeriodCode"]?.ToString(),
                BillingPeriodLabel = header["BillingPeriodLabel"]?.ToString(),
                BillingPeriod = billingPeriod,
                BillingPeriodMonths = billingPeriodMonths,
                NumberOfMonths = numberOfMonths,
                SeatPrice = Convert.ToDecimal(header["SeatPrice"]),
                RoomPrice = Convert.ToDecimal(header["RoomPrice"]),
                MonthlyRent = monthlyRent,
                CurrentCycleAmount = currentCycleAmount,
                SecurityDeposit = securityDeposit,
                DiscountPercentage = header.TryGetValue("DiscountPercentage", out var dp) && dp is not null ? Convert.ToDecimal(dp) : 0,
                DiscountAmount = header.TryGetValue("DiscountAmount", out var da) && da is not null ? Convert.ToDecimal(da) : 0,
                SubtotalAmount = header.TryGetValue("SubtotalAmount", out var sa) && sa is not null ? Convert.ToDecimal(sa) : 0,
                WithholdingTaxRate = header.TryGetValue("WithholdingTaxRate", out var wtr) && wtr is not null && Convert.ToDecimal(wtr) > 0 ? Convert.ToDecimal(wtr) : 15.00m,
                TotalContractAmount = totalContractAmount,
                TotalPaidAmount = totalPaidAmount,
                BalanceLeft = balanceLeft,
                NextBillDueDate = nextBillDueDate,
                NextBillingDate = nextBillingDate ?? nextBillDueDate
            };

            decimal lineSum = 0;
            foreach (var line in lines)
            {
                var lineTotal = Convert.ToDecimal(line["LineTotal"]);
                lineSum += lineTotal;

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

            dto.DiscountAmount = Math.Abs(dto.DiscountAmount);
            WorkNest.Application.Services.ChallanCalculationService.ApplyToChallan(dto);

            if (dto.IsMeetingRoom)
            {
                dto.BalanceLeft = 0;
                dto.NextBillDueDate = null;
                dto.NextBillingDate = null;
                dto.Contract = null;
                dto.TimeSlot = dto.StartOn.HasValue && dto.EndOn.HasValue
                    ? $"{dto.StartOn.Value:hh:mm tt} - {dto.EndOn.Value:hh:mm tt}"
                    : null;
            }
            else
            {
                dto.Contract = new ContractDetailsDto
                {
                    ContractStartDate = dto.ContractStartDate ?? dto.StartOn,
                    ContractEndDate = dto.ContractEndDate ?? dto.EndOn,
                    NumberOfMonths = dto.NumberOfMonths,
                    MonthlyRent = dto.MonthlyRent,
                    BillingPeriod = dto.BillingPeriod ?? dto.BillingPeriodLabel ?? dto.BillingPeriodCode,
                    BillingPeriodMonths = dto.BillingPeriodMonths,
                    CurrentCycleAmount = dto.CurrentCycleAmount,
                    TotalContractAmount = dto.TotalContractAmount,
                    NextBillingDate = dto.NextBillingDate ?? dto.NextBillDueDate,
                    NextBillDueDate = dto.NextBillDueDate,
                    BalanceLeft = dto.BalanceLeft,
                    SecurityDeposit = dto.SecurityDeposit,
                    SpaceNumber = dto.SpaceNumber
                };
            }
            return ApiResponse.Ok(dto);
        }

        public async Task<ApiResponse> SendChallanEmailAsync(int bookingId, byte[]? pdfBytes = null)
        {
            var challanResult = await GetChallanAsync(bookingId);
            if (!challanResult.IsSuccessful) return challanResult;

            var dto = (ChallanResponseDto)challanResult.Data!;

            if (string.IsNullOrWhiteSpace(dto.CustomerEmail))
                return ApiResponse.Fail("Customer email not found on challan.");

            var pdf = pdfBytes != null && pdfBytes.Length > 0
                ? pdfBytes
                : _pdf.GenerateBookingConfirmationPdf(dto);

            await _email.SendChallanEmailAsync(
                dto.CustomerEmail,
                dto.CustomerName ?? "Customer",
                dto.ChallanNumber,
                dto.SpaceName ?? dto.SpaceCode ?? "",
                dto.BillingPeriodLabel ?? dto.BillingPeriodCode ?? "",
                dto.TotalPayable,
                dto.StartOn,
                dto.EndOn,
                dto.TotalContractAmount,
                dto.NextBillDueDate,
                dto.BalanceLeft,
                dto.CurrentCycleAmount,
                dto.SecurityDeposit,
                dto.TaxAmount,
                dto.DiscountAmount,
                pdf);

            return ApiResponse.Ok("Challan email sent successfully.");
        }

        public async Task<byte[]> GenerateAdvanceInvoicePdfAsync(int bookingId, int advanceMonths, int secDepositMonths, decimal monthlyRate, decimal discountAmount)
        {
            var challanResult = await GetChallanAsync(bookingId);
            var dto = challanResult.IsSuccessful ? (ChallanResponseDto)challanResult.Data! : null;

            var start = dto?.StartOn ?? _clock.Today;
            var months = new List<AdvanceInvoiceMonthDto>();
            decimal advTotal = 0m;

            if (start.Day > 1 && monthlyRate > 0)
            {
                int startDay = start.Day;
                int daysInMonth = DateTime.DaysInMonth(start.Year, start.Month);
                int remainingDays = daysInMonth - startDay; // from the day after the start day (20th of 30 = 10 days)
                decimal proratedMonth1 = Math.Round(((decimal)remainingDays / daysInMonth) * monthlyRate, 2);

                months.Add(new AdvanceInvoiceMonthDto
                {
                    MonthName = $"{start:MMMM yyyy} (Prorated: {remainingDays}/{daysInMonth} days)",
                    Amount    = proratedMonth1
                });

                if (startDay < 15)
                {
                    // Current month counts as first billing month: 1 prorated + (advanceMonths - 1) full months
                    int fullMonths = Math.Max(0, advanceMonths - 1);
                    for (int i = 1; i <= fullMonths; i++)
                    {
                        var d = new DateTime(start.Year, start.Month, 1).AddMonths(i);
                        months.Add(new AdvanceInvoiceMonthDto
                        {
                            MonthName = d.ToString("MMMM yyyy"),
                            Amount    = monthlyRate
                        });
                    }
                    advTotal = proratedMonth1 + (fullMonths * monthlyRate);
                }
                else
                {
                    // Current month is separate prorated period: 1 prorated + advanceMonths full months
                    for (int i = 1; i <= advanceMonths; i++)
                    {
                        var d = new DateTime(start.Year, start.Month, 1).AddMonths(i);
                        months.Add(new AdvanceInvoiceMonthDto
                        {
                            MonthName = d.ToString("MMMM yyyy"),
                            Amount    = monthlyRate
                        });
                    }
                    advTotal = proratedMonth1 + (advanceMonths * monthlyRate);
                }
            }
            else
            {
                for (int i = 0; i < advanceMonths; i++)
                {
                    var d = start.AddMonths(i);
                    months.Add(new AdvanceInvoiceMonthDto
                    {
                        MonthName = d.ToString("MMMM yyyy"),
                        Amount    = monthlyRate
                    });
                }
                advTotal = monthlyRate * advanceMonths;
            }

            // Task 1: Mirror discount to security deposit (Deposit is full months, not prorated)
            decimal baseSecTotal = monthlyRate * secDepositMonths;
            decimal depositDiscountPct = (dto?.DiscountPercentage > 0)
                ? dto.DiscountPercentage
                : ((monthlyRate > 0 && discountAmount > 0 && advanceMonths > 0)
                    ? Math.Min(100m, (discountAmount / (monthlyRate * advanceMonths)) * 100m)
                    : 0m);
            decimal secTotal = baseSecTotal > 0
                ? Math.Max(0, Math.Round(baseSecTotal * (1 - (depositDiscountPct / 100.0m)), 2))
                : 0m;

            // Task 2: Support charge and PST
            int capacity = dto?.SpaceCapacity > 0 ? dto.SpaceCapacity : 1;
            decimal perSeatSupportRate = 2000.00m;
            decimal taxPercentage = dto?.AppliedTaxPercentage > 0 ? dto.AppliedTaxPercentage : 16.00m;
            decimal supportChargeTotal = Math.Round(perSeatSupportRate * capacity * advanceMonths, 2);
            decimal taxTotal = Math.Round(supportChargeTotal * (taxPercentage / 100.0m), 2);

            decimal totalPay = Math.Max(0, advTotal + secTotal + taxTotal - discountAmount);

            var invDto = new AdvanceInvoicePdfDto
            {
                InvoiceNumber         = $"ADV-{bookingId}-{_clock.Today:yyyyMMdd}",
                CustomerName          = dto?.CustomerName ?? "Customer",
                CustomerEmail         = dto?.CustomerEmail ?? "",
                SpaceName             = dto?.SpaceName ?? dto?.SpaceCode ?? "",
                SpaceTypeName         = dto?.SpaceTypeName ?? "",
                LocationName          = dto?.LocationName ?? "",
                StartOn               = dto?.StartOn ?? _clock.Today,
                EndOn                 = dto?.EndOn ?? _clock.Today,
                DueOn                 = _clock.Today.AddDays(7), // invoice date + 7 days, like every invoice
                AdvanceRentMonths     = advanceMonths,
                SecurityDepositMonths = secDepositMonths,
                AdvanceRentTotal      = advTotal,
                SecurityDepositTotal  = secTotal,
                SupportChargeTotal    = supportChargeTotal,
                AppliedTaxPercentage  = taxPercentage,
                TaxTotal              = taxTotal,
                DiscountAmount        = discountAmount,
                TotalPayable          = totalPay,
                WithholdingTaxRate    = dto?.WithholdingTaxRate ?? 15.00m,
                MonthsBreakdown       = months,
                IssuedOn              = _clock.Today
            };

            return _pdf.GenerateAdvanceInvoicePdf(invDto);
        }

        public async Task<ApiResponse> SendBookingConfirmationEmailAsync(int bookingId)
        {
            var challanResult = await GetChallanAsync(bookingId);
            if (!challanResult.IsSuccessful) return challanResult;

            var dto = (ChallanResponseDto)challanResult.Data!;

            if (string.IsNullOrWhiteSpace(dto.CustomerEmail))
                return ApiResponse.Fail("Customer email not found on booking.");

            var pdf = _pdf.GenerateBookingConfirmationPdf(dto);

            await _email.SendBookingConfirmationAsync(
                dto.CustomerEmail,
                dto.CustomerName ?? "Customer",
                dto.ChallanNumber,
                dto.SpaceName ?? dto.SpaceCode ?? "",
                dto.StartOn,
                dto.EndOn,
                dto.BillingPeriodLabel ?? dto.BillingPeriod ?? "",
                dto.TotalPayable,
                dto.CurrentCycleAmount,
                dto.SecurityDeposit,
                dto.TaxAmount,
                dto.DiscountAmount,
                dto.TotalContractAmount,
                dto.NextBillDueDate,
                dto.BalanceLeft,
                pdf);

            return ApiResponse.Ok("Booking confirmation email sent successfully.");
        }

        // --- Helpers ---

        private async Task<int> ResolvePricingIdAsync(int spaceId)
        {
            var pricing = await _db.GetActivePricingForSpaceAsync(spaceId);
            return pricing?.TryGetValue("PricingId", out var pid) == true && pid is not null
                ? Convert.ToInt32(pid) : 0;
        }

        public async Task<ApiResponse> GetBookingFinancialBreakdownAsync(int bookingId)
        {
            var challanRes = await GetChallanAsync(bookingId);
            if (!challanRes.IsSuccessful || challanRes.Data is not ChallanResponseDto dto)
                return ApiResponse.Fail("Booking/Challan not found");

            double durationHours = dto.StartOn.HasValue && dto.EndOn.HasValue ? (dto.EndOn.Value - dto.StartOn.Value).TotalHours : 0;
            decimal quantity = dto.IsMeetingRoom ? (decimal)(durationHours >= 24 ? Math.Ceiling(durationHours / 24.0) : Math.Ceiling(durationHours)) : dto.BillingPeriodMonths;
            if (quantity <= 0) quantity = 1;
            decimal unitPrice = dto.SubtotalAmount > 0 && quantity > 0 ? Math.Round(dto.SubtotalAmount / quantity, 2) : dto.SubtotalAmount;
            decimal supportAmt = dto.SupportChargeAmount > 0 ? dto.SupportChargeAmount : (dto.IsMeetingRoom ? Math.Round(dto.SubtotalAmount * 0.10m, 2) : Math.Round(2000.00m * (dto.SpaceCapacity > 0 ? dto.SpaceCapacity : 1) * dto.BillingPeriodMonths, 2));

            var breakdown = new
            {
                bookingGuid = dto.BookingPublicId ?? dto.ChallanPublicId,
                challanNumber = dto.ChallanNumber,
                startDateTime = dto.StartOn,
                endDateTime = dto.EndOn,
                calculatedDuration = dto.IsMeetingRoom ? (durationHours >= 24 ? $"{quantity} Days" : $"{durationHours} Hours") : $"{dto.BillingPeriodMonths} Months",
                quantity = quantity,
                unitPrice = unitPrice,
                baseAmount = dto.SubtotalAmount,
                supportServiceAmount = supportAmt,
                taxableAmount = supportAmt,
                pstRate = dto.AppliedTaxPercentage > 0 ? dto.AppliedTaxPercentage : 16.00m,
                pstAmount = dto.TaxAmount,
                totalAmount = dto.TotalPayable
            };

            return ApiResponse.Ok(breakdown);
        }

        private static DateTime ParseFlexibleDate(string? dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return default;
            if (DateTime.TryParse(dateStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                return dt;
            if (DateTime.TryParse(dateStr, out var dtLocal))
                return dtLocal;
            return default;
        }
    }

}