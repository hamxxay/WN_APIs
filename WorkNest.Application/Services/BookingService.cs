using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.DTOs.Payment;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class BookingService : IBookingService
    {
        private readonly IDbRepository _db;
        private readonly IEmailService _email;
        private readonly IPdfService _pdf;
        public BookingService(IDbRepository db, IEmailService email, IPdfService pdf) { _db = db; _email = email; _pdf = pdf; }

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

            var result = await _db.InsertBookingAsync(
                userId.Value, request.SpaceId ?? 0, pricingId,
                request.StartDateTime, request.EndDateTime,
                request.Notes, userId, userEmail,
                request.FirstName != null ? userEmail : null,
                request.FirstName, request.LastName, request.PhoneNumber,
                request.CnicOrPassport, request.Address, request.CityId,
                "Created during self-booking");

            if (result.TryGetValue("ErrorMessage", out var err) && err is not null && !string.IsNullOrWhiteSpace(err.ToString()))
                return ApiResponse.Fail(err.ToString() ?? "An error occurred");

            return ApiResponse.Ok(result, "Booking created.");
        }

        public async Task<ApiResponse> CreateAdminBookingAsync(AdminBookingRequest request, string? actorEmail)
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

            DateTime startOn = request.StartDateTime ?? ParseFlexibleDate(
                request.StartDate ?? request.StartOn ?? request.ContractStartDate ?? request.BillingStartDate ?? request.EffectiveFrom);

            DateTime endOn = request.EndDateTime ?? ParseFlexibleDate(
                request.EndDate ?? request.EndOn ?? request.ContractEndDate);

            if (startOn == default)
            {
                startOn = DateTime.UtcNow.Date;
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
                startOn, endOn,
                request.Notes, actorId, request.CustomerEmail,
                request.CustomerEmail, firstName, lastName, request.Phone,
                null, null, null, "Created by administrator",
                discountType == "Percentage" ? discountValue : 0,
                discountType, discountValue,
                request.SecurityDepositOverride, request.FloorId, request.BillingPeriodMonths, request.SecurityDepositMonths, request.AdvanceRentMonths);

            if (result.TryGetValue("ErrorMessage", out var err) && err is not null && !string.IsNullOrWhiteSpace(err.ToString()))
                return ApiResponse.Fail(err.ToString() ?? "An error occurred");

            return ApiResponse.Ok(result, "Admin booking created.");
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

            var result = await _db.InsertSmartBookingAsync(
                userEmail, request.CategoryCode,
                request.StartDateTime, request.EndDateTime,
                request.Capacity, request.Notes, userId,
                request.FirstName != null ? userEmail : null,
                request.FirstName, request.LastName, request.PhoneNumber,
                request.CnicOrPassport, request.Address, request.CityId,
                "Created during self-booking");

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
            var billingPeriod = header.TryGetValue("BillingPeriod", out var bp) && bp != null ? bp.ToString() : (header["BillingPeriodLabel"]?.ToString() ?? header["BillingPeriodCode"]?.ToString());

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

            decimal supportTaxOnCycle = Math.Round(Math.Round(currentCycleAmount * 0.10m, 2) * 0.16m, 2);
            decimal totalPayableInitial = Math.Max(0, currentCycleAmount + securityDeposit + supportTaxOnCycle);

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

            var advTotal = monthlyRate * advanceMonths;
            var secTotal = monthlyRate * secDepositMonths;
            var totalPay = advTotal + secTotal - discountAmount;

            var start = dto?.StartOn ?? DateTime.UtcNow;
            var months = new List<AdvanceInvoiceMonthDto>();
            for (int i = 0; i < advanceMonths; i++)
            {
                var d = start.AddMonths(i);
                months.Add(new AdvanceInvoiceMonthDto
                {
                    MonthName = d.ToString("MMMM yyyy"),
                    Amount    = monthlyRate
                });
            }

            var invDto = new AdvanceInvoicePdfDto
            {
                InvoiceNumber         = $"ADV-{bookingId}-{DateTime.UtcNow:yyyyMMdd}",
                CustomerName          = dto?.CustomerName ?? "Customer",
                CustomerEmail         = dto?.CustomerEmail ?? "",
                SpaceName             = dto?.SpaceName ?? dto?.SpaceCode ?? "",
                SpaceTypeName         = dto?.SpaceTypeName ?? "",
                LocationName          = dto?.LocationName ?? "",
                StartOn               = dto?.StartOn ?? DateTime.UtcNow,
                EndOn                 = dto?.EndOn ?? DateTime.UtcNow,
                AdvanceRentMonths     = advanceMonths,
                SecurityDepositMonths = secDepositMonths,
                AdvanceRentTotal      = advTotal,
                SecurityDepositTotal  = secTotal,
                DiscountAmount        = discountAmount,
                TotalPayable          = totalPay,
                MonthsBreakdown       = months,
                IssuedOn              = DateTime.UtcNow
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
            decimal supportAmt = Math.Round(dto.SubtotalAmount * 0.10m, 2);

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