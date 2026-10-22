using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.Helpers;
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
            (await GetMyBookingRowsAsync(userEmail)).Rows.Cast<object>();

        /// <summary>Open door-access suspensions of these bookings; none when they can't be read (a notice must never break the list).</summary>
        private async Task<IDictionary<int, BookingAccessSuspensionInfo>> SuspensionsOfAsync(IEnumerable<int> bookingIds)
        {
            try { return await _db.GetOpenAccessSuspensionsDbAsync(bookingIds, _clock.Today); }
            catch (Exception) { return new Dictionary<int, BookingAccessSuspensionInfo>(); }
        }

        /// <summary>
        /// GET booking/my rows with TotalPayable / TotalPaidAmount and the door-access notice: AccessSuspended,
        /// AccessSuspendedReason (plain text for the customer), AccessSuspendedInvoiceId / AccessSuspendedInvoiceNumber
        /// (the unpaid invoice to open). Rejected / cancelled bookings never carry the notice.
        /// </summary>
        private async Task<(List<IDictionary<string, object?>> Rows, IDictionary<int, BookingAccessSuspensionInfo> Suspensions)> GetMyBookingRowsAsync(string userEmail)
        {
            var rows = (await _db.GetMyBookingsAsync(userEmail)).ToList();

            // TotalPayable = the amount on the booking's challan, exactly as GET booking/{id}/challan returns it
            // (same procedure, same calculation), read for all the bookings in one round trip. Null when there is no challan.
            var ids = rows.Select(r => r.TryGetValue("BookingId", out var v) && v is not null ? Convert.ToInt32(v) : 0).ToList();
            var challans = await _db.GetChallansWithDetailsAsync(ids);
            // TotalPaidAmount: what is recorded as paid on the booking (0 = nothing; the customer cancel is refused when > 0).
            var paid = await _db.GetBookingPaidAmountsDbAsync(ids);
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i]["TotalPaidAmount"] = paid.TryGetValue(ids[i], out var paidAmount) ? paidAmount : 0m;
                decimal? totalPayable = null;
                if (challans.TryGetValue(ids[i], out var ch) && ch.ChallanHeader is not null)
                {
                    try { totalPayable = (await BuildChallanAsync(ch.ChallanHeader, ch.Lines)).TotalPayable; }
                    catch (Exception) { totalPayable = null; } // one odd challan must not break the whole list
                }
                rows[i]["TotalPayable"] = totalPayable;
            }

            var suspensions = await SuspensionsOfAsync(ids);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                bool closed = CustomerBills.IsClosedBooking(RowText(row, "BookingStatusCode"), RowText(row, "BookingStatusLabel"));
                var s = !closed && suspensions.TryGetValue(ids[i], out var found) ? found : null;
                row["AccessSuspended"] = s != null;
                row["AccessSuspendedReason"] = s != null ? CustomerBills.SuspensionMessage(s.InvoiceNumber, s.InvoiceDueOn, RowText(row, "ChallanNumber")) : null;
                row["AccessSuspendedInvoiceId"] = s?.InvoiceId;
                row["AccessSuspendedInvoiceNumber"] = s?.InvoiceNumber;
            }
            return (rows, suspensions);
        }

        private static string? RowText(IDictionary<string, object?> row, string key) =>
            row.TryGetValue(key, out var v) && v is not null && v is not DBNull && !string.IsNullOrWhiteSpace(v.ToString()) ? v.ToString()!.Trim() : null;

        private static decimal RowDecimal(IDictionary<string, object?> row, string key) =>
            row.TryGetValue(key, out var v) && v is not null && v is not DBNull ? Convert.ToDecimal(v) : 0m;

        private static int? RowInt(IDictionary<string, object?> row, string key) =>
            row.TryGetValue(key, out var v) && v is not null && v is not DBNull ? Convert.ToInt32(v) : null;

        private static DateTime? RowDate(IDictionary<string, object?> row, string key) =>
            row.TryGetValue(key, out var v) ? ParseDateSafely(v is DBNull ? null : v) : null;

        public async Task<MyBillsDto> GetMyBillsAsync(string userEmail)
        {
            var today = _clock.Today;
            var now = _clock.Now;
            var invoiceRows = await _db.GetMyInvoicesDbAsync(userEmail);
            var (bookingRows, suspensions) = await GetMyBookingRowsAsync(userEmail);
            var bills = new List<MyBillDto>();

            foreach (var r in invoiceRows)
            {
                int statusId = RowInt(r, "StatusId") ?? 0;
                if (statusId == 40) continue;
                var statusDescription = RowText(r, "StatusDescription");
                decimal total = Math.Round(RowDecimal(r, "GrandTotal"), 2), withheld = Math.Round(RowDecimal(r, "WHTAmount"), 2), paid = Math.Round(RowDecimal(r, "PaidTotal"), 2);
                var dueOn = RowDate(r, "DueOn")?.Date;
                var state = CustomerBills.ClassifyInvoice(statusId, statusDescription, total, withheld, paid, dueOn, today);
                if (state.IsVoid && paid < 1m) continue; // a voided invoice with nothing paid on it is not a bill any more

                int? bookingId = RowInt(r, "BookingId");
                bool closed = CustomerBills.IsClosedBooking(RowText(r, "BookingStatusCode"), RowText(r, "BookingStatusLabel"));
                int invoiceId = RowInt(r, "Id") ?? 0;
                var s = bookingId.HasValue && !closed && suspensions.TryGetValue(bookingId.Value, out var found) ? found : null;
                bool suspendedNow = s != null && s.InvoiceId == invoiceId;
                DateTime? suspendsOn = null;
                if (!closed && !suspendedNow && bookingId.HasValue && dueOn.HasValue && state.Balance >= 1m
                    && CustomerBills.CountsForSuspension(statusId, statusDescription))
                {
                    var on = CustomerBills.AccessSuspendsOn(dueOn.Value);
                    if (on > today) suspendsOn = on;
                }

                bills.Add(new MyBillDto
                {
                    Kind = "invoice",
                    InvoiceId = invoiceId,
                    InvoicePublicId = r.TryGetValue("PublicId", out var pid) && pid is Guid g ? g : null,
                    Number = RowText(r, "InvoiceNumber") ?? $"Invoice #{invoiceId}",
                    Title = CustomerBills.InvoiceTitle(RowInt(r, "InvoiceTypeId")),
                    BookingId = bookingId,
                    BookingStatus = RowText(r, "BookingStatusLabel") ?? RowText(r, "BookingStatusCode"),
                    BookingClosed = closed,
                    SpaceName = RowText(r, "SpaceName"),
                    SpaceCode = RowText(r, "SpaceCode"),
                    LocationName = RowText(r, "LocationName"),
                    IssuedOn = RowDate(r, "IssuedOn")?.Date,
                    DueOn = dueOn,
                    PeriodStart = RowDate(r, "PeriodStart"),
                    PeriodEnd = RowDate(r, "PeriodEnd"),
                    CurrencyCode = RowText(r, "CurrencyCode") ?? "PKR",
                    Total = total,
                    TaxWithheld = withheld,
                    // Marked paid in the database (bank / fee receipt) counts as paid in full.
                    Paid = state.Status == CustomerBills.Paid ? Math.Max(paid, total - withheld) : paid,
                    Balance = state.Balance,
                    Status = state.Status,
                    IsOverdue = state.IsOverdue,
                    DaysOverdue = state.DaysOverdue,
                    AccessSuspendsOn = suspendsOn,
                    AccessSuspended = suspendedNow,
                    HasPdf = invoiceId > 0
                });
            }

            // Booking challans with no invoice at all (older bookings): the challan is the bill.
            var invoicedBookings = new HashSet<int>(invoiceRows.Select(r => RowInt(r, "BookingId") ?? 0).Where(id => id > 0));
            foreach (var row in bookingRows)
            {
                int bookingId = RowInt(row, "BookingId") ?? 0;
                var challanNumber = RowText(row, "ChallanNumber");
                if (bookingId <= 0 || challanNumber == null || invoicedBookings.Contains(bookingId)) continue;
                if (!row.TryGetValue("TotalPayable", out var tp) || tp is null) continue; // amount unknown: not shown as a bill
                decimal total = Math.Round(Convert.ToDecimal(tp), 2), paid = Math.Round(RowDecimal(row, "TotalPaidAmount"), 2);
                var code = RowText(row, "BookingStatusCode");
                var label = RowText(row, "BookingStatusLabel");
                bool closed = CustomerBills.IsClosedBooking(code, label);
                bool pending = CustomerBills.IsPendingBooking(code, label);
                var validUntil = RowDate(row, "ChallanValidUntil");
                var state = CustomerBills.ClassifyChallan(pending, closed, total, paid, validUntil, now);
                if (state.IsVoid) continue; // rejected / cancelled with nothing paid: nothing to show
                var bookedOn = RowDate(row, "BookedOn");
                bool suspendedNow = !closed && suspensions.TryGetValue(bookingId, out var s) && s.InvoiceId == null;

                bills.Add(new MyBillDto
                {
                    Kind = "challan",
                    Number = challanNumber,
                    Title = "Booking challan",
                    BookingId = bookingId,
                    BookingStatus = label ?? code,
                    BookingClosed = closed,
                    SpaceName = RowText(row, "SpaceName"),
                    SpaceCode = RowText(row, "SpaceCode"),
                    LocationName = RowText(row, "LocationName"),
                    // WN_Bookings.CreatedOn is UTC; shown as the Pakistan date.
                    IssuedOn = bookedOn.HasValue ? _clock.FromUtc(bookedOn.Value).Date : null,
                    DueOn = validUntil,
                    PeriodStart = RowDate(row, "StartOn"),
                    PeriodEnd = RowDate(row, "EndOn"),
                    Total = total,
                    // A booking is confirmed once its challan is paid.
                    Paid = state.Status == CustomerBills.Paid && !closed ? Math.Max(paid, total) : paid,
                    Balance = state.Balance,
                    Status = state.Status,
                    AccessSuspended = suspendedNow,
                    HasPdf = false
                });
            }

            var sorted = CustomerBills.Sort(bills);
            return new MyBillsDto { Items = sorted, Summary = CustomerBills.Summarize(sorted) };
        }

        public async Task<IEnumerable<object>> GetRecentBookingsAsync(int top = 10) =>
            (await _db.GetRecentBookingsAsync(top)).Cast<object>();

        public async Task<ApiResponse> GetBookingByIdAsync(Guid publicId, string? userEmail)
        {
            var data = await _db.GetBookingByPublicIdAsync(publicId, userEmail);
            if (data is null) return ApiResponse.Fail("Booking not found");
            // Same flag and door-access notice as GET booking/{id}: the row is WN_vw_BookingSummary (v.*).
            data["BillingFiguresReliable"] = BillingFiguresReliable(data);
            int bookingId = RowInt(data, "BookingId") ?? 0;
            var s = bookingId > 0 && !CustomerBills.IsClosedBooking(RowText(data, "BookingStatusCode"), RowText(data, "BookingStatusLabel"))
                    && (await SuspensionsOfAsync(new[] { bookingId })).TryGetValue(bookingId, out var found) ? found : null;
            data["AccessSuspended"] = s != null;
            data["AccessSuspendedReason"] = s != null ? CustomerBills.SuspensionMessage(s.InvoiceNumber, s.InvoiceDueOn, RowText(data, "ChallanNumber")) : null;
            data["AccessSuspendedInvoiceId"] = s?.InvoiceId;
            data["AccessSuspendedInvoiceNumber"] = s?.InvoiceNumber;
            return ApiResponse.Ok(data);
        }

        public async Task<ApiResponse> GetBookingCalendarAsync(int spaceId, int year, int month, bool includeBookings = false)
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
            // Booker names / emails are for staff only; the public calendar gets the booked dates.
            return includeBookings ? ApiResponse.Ok(new { bookedDates, bookings = result }) : ApiResponse.Ok(new { bookedDates });
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

        public async Task<ApiResponse> GetSmartAvailableSpacesAsync(string categoryCode, DateTime startOn, DateTime endOn, int? capacity, string? shiftType = "24_7", int? locationId = null)
        {
            var result = await _db.GetSmartAvailableSpacesAsync(categoryCode, startOn, endOn, capacity, shiftType, locationId);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> CreateBookingAsync(BookingRequest request, string userEmail)
        {
            var cnicError = Cnic.Validate(request.CnicOrPassport);
            if (cnicError != null) return ApiResponse.Fail(cnicError);
            var phoneError = Phone.Validate(request.PhoneNumber);
            if (phoneError != null) return ApiResponse.Fail(phoneError);

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

            var shiftType = !string.IsNullOrWhiteSpace(request.ShiftType) ? request.ShiftType : null;
            if (string.IsNullOrWhiteSpace(shiftType) && request.OfferingTypeId.HasValue)
            {
                int otId = request.OfferingTypeId.Value;
                if (otId == 2 || otId == 5) shiftType = "morning";
                else if (otId == 3 || otId == 6) shiftType = "evening";
                else if (otId == 1 || otId == 4) shiftType = "24_7";
            }
            if (string.IsNullOrWhiteSpace(shiftType))
            {
                shiftType = !string.IsNullOrWhiteSpace(request.OfferingType) ? request.OfferingType : "24_7";
            }

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

            var phoneError = Phone.Validate(request.Phone);
            if (phoneError != null) return ApiResponse.Fail(phoneError);

            // Security Deposit (Months): 0, 1, 2 or 3 only.
            if (!SecurityDepositRules.IsValidMonths(request.SecurityDepositMonths))
                return ApiResponse.Fail(SecurityDepositRules.InvalidMonthsMessage);

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
            var (standardPerSeat, spaceCapacity, categoryCode, spaceName, spaceTypeName) = await GetSpacePricingAsync(spaceId);
            bool isPrivate = categoryCode is "PrivateOffice" or "Private";
            // Deposit months for the space kind: private office / shared space 0-3 (default 2), meeting room always 0.
            // WN_Bookings_Insert and the update after it work the amount out from these months (no override).
            int securityDepositMonths = SecurityDepositRules.ResolveMonths(
                SecurityDepositRules.KindOf(categoryCode, spaceTypeName), request.SecurityDepositMonths);
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
                var offering = (request.OfferingTypeId.HasValue && request.OfferingTypeId.Value > 0)
                    ? offeringTypes.FirstOrDefault(o => Convert.ToInt32(o["Id"]) == request.OfferingTypeId.Value)
                    : (int.TryParse(request.OfferingType, out int otId)
                        ? offeringTypes.FirstOrDefault(o => Convert.ToInt32(o["Id"]) == otId)
                        : (offeringTypes.FirstOrDefault(o => string.Equals(o["Description"]?.ToString(), request.OfferingType, StringComparison.OrdinalIgnoreCase))
                           ?? offeringTypes.FirstOrDefault()));
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

            // WhtRate is the Id of the chosen WN_WHTaxRate row (the dropdown), not a percentage.
            if (request.SendWhtInvoice && !await _db.IsActiveWhtTaxRateIdAsync(request.WhtRate))
                return ApiResponse.Fail("Select a WHT rate from the list.");

            var result = await _db.InsertBookingAsync(
                userId, spaceId, pricingId,
                startOn, endOn,
                request.Notes, actorId, request.CustomerEmail,
                request.CustomerEmail, firstName, lastName, request.Phone,
                null, null, null, "Created by administrator",
                discountType == "Percentage" ? discountValue : 0,
                discountType, discountValue,
                null, request.FloorId, request.BillingPeriodMonths, securityDepositMonths, request.AdvanceRentMonths,
                shiftType, capacity, perSeatBasePrice, request.SendWhtInvoice, request.SendWhtInvoice ? request.WhtRate : null);

            if (result.TryGetValue("ErrorMessage", out var err) && err is not null && !string.IsNullOrWhiteSpace(err.ToString()))
                return ApiResponse.Fail(err.ToString() ?? "An error occurred");

            return ApiResponse.Ok(result, "Admin booking created.");
        }

        /// <summary>Standard per-seat monthly price (s.Price, as WN_Bookings_Insert uses), capacity, category and type name of a space.</summary>
        private async Task<(decimal StandardPerSeat, int Capacity, string CategoryCode, string Name, string SpaceTypeName)> GetSpacePricingAsync(int spaceId)
        {
            var (rows, _) = await _db.GetSpacesAsync(1, 10000, null);
            var row = rows.FirstOrDefault(r => r.TryGetValue("Id", out var id) && id is not null && Convert.ToInt32(id) == spaceId);
            if (row is null) return (0m, 1, "", "this space", "");
            decimal price = row.TryGetValue("Price", out var p) && p is not null ? Convert.ToDecimal(p) : 0m;
            int cap = row.TryGetValue("Capacity", out var c) && c is not null ? Convert.ToInt32(c) : 1;
            string category = row.TryGetValue("CategoryCode", out var cc) ? cc?.ToString() ?? "" : "";
            string name = row.TryGetValue("Name", out var n) ? n?.ToString() ?? "this space" : "this space";
            string typeName = row.TryGetValue("SpaceTypeName", out var tn) ? tn?.ToString() ?? "" : "";
            return (price, Math.Max(1, cap), category, name, typeName);
        }

        public async Task<ApiResponse> CreateSmartBookingAsync(SmartBookingRequest request, string userEmail)
        {
            var cnicError = Cnic.Validate(request.CnicOrPassport);
            if (cnicError != null) return ApiResponse.Fail(cnicError);
            var phoneError = Phone.Validate(request.PhoneNumber);
            if (phoneError != null) return ApiResponse.Fail(phoneError);

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
                smartShiftType,
                request.LocationId is > 0 ? request.LocationId : null);

            if (result.TryGetValue("ErrorMessage", out var err) && err is not null && !string.IsNullOrWhiteSpace(err.ToString()))
                return ApiResponse.Fail(err.ToString() ?? "An error occurred");

            // Sent again within 2 minutes (e.g. the app timed out and the customer retried): the booking made a moment
            // ago comes back instead of a second one (WN_Bookings_InsertSmart, QA_03).
            if (SmartBookingRules.IsDuplicate(result))
                return ApiResponse.Ok(result, SmartBookingRules.DuplicateMessage);

            return ApiResponse.Ok(result, "Smart booking created.");
        }

        public async Task<ApiResponse> UpdateBookingAsync(int id, BookingUpdateRequest request, int? actorId)
        {
            // Security Deposit (Months): 0, 1, 2 or 3 only; checked before anything is saved.
            if (!SecurityDepositRules.IsValidMonths(request.SecurityDepositMonths))
                return ApiResponse.Fail(SecurityDepositRules.InvalidMonthsMessage);

            // A new month count recomputes the stored deposit (WN_Bookings_UpdateSecurityDeposit, SD_01: shared spaces
            // and meeting rooms stay 0; the same months change nothing; refused once the booking has an invoice, as
            // issued invoices never change). Done first so a refusal leaves the whole booking unchanged.
            IDictionary<string, object?>? deposit = null;
            if (request.SecurityDepositMonths.HasValue)
            {
                deposit = await _db.UpdateBookingSecurityDepositAsync(id, request.SecurityDepositMonths.Value, actorId);
                if (deposit.TryGetValue("ErrorMessage", out var err) && err is not null && err is not DBNull && !string.IsNullOrWhiteSpace(err.ToString()))
                    return ApiResponse.Fail(err.ToString()!);
            }

            await _db.UpdateBookingAsync(id, request.StartDateTime, request.EndDateTime, request.Notes, actorId);
            return deposit is { Count: > 0 } ? ApiResponse.Ok(deposit, "Booking updated.") : ApiResponse.Ok("Booking updated.");
        }

        public async Task<ApiResponse> UpdateBookingStatusAsync(int id, byte statusId, int? actorId)
        {
            // Only the statuses of dbo.WN_BookingStatuses (looked up, never a list of numbers here): the old 1 / 2 / 4 and
            // any other id are refused, so they can never be written again.
            var statuses = await _db.GetBookingStatusesDbAsync() ?? Array.Empty<(int Id, string Code)>();
            if (!statuses.Any(s => s.Id == statusId))
                return ApiResponse.Fail(statuses.Count == 0
                    ? $"Booking status {statusId} is not a booking status."
                    : $"Booking status {statusId} is not a booking status. Use one of: {string.Join(", ", statuses.Select(s => $"{s.Id} {s.Code}"))}.");
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
                // The advance rent on the booking's challan (same calculation as GET booking/{id}/challan: a booking
                // that starts after the 1st also pays the rest of its first month), so the two screens agree.
                FirstCycleRent = calcResult.FirstCycleRent > 0m ? calcResult.FirstCycleRent : currentCycleAmount,
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

            // Same challan number / validity as GET booking/{guid} and GET booking/my (WN_vw_BookingSummary).
            var summary = await _db.GetBookingSummaryRowDbAsync(result.BookingId);
            if (summary is not null)
            {
                result.ChallanNumber = summary.TryGetValue("ChallanNumber", out var chn) && chn is not null ? chn.ToString() : null;
                result.ChallanValidUntil = ParseDateSafely(summary.TryGetValue("ChallanValidUntil", out var chv) ? chv : null);
                // The stored Security Deposit (Months); 0 when there is no deposit.
                result.SecurityDepositMonths = result.SecurityDeposit > 0 && summary.TryGetValue("SecurityDepositMonths", out var sdm)
                    && sdm is not null && sdm is not DBNull ? Convert.ToInt32(sdm) : 0;
            }
            result.BillingFiguresReliable = BillingFiguresReliable(summary);

            // Door access suspended for an unpaid bill (not shown on rejected / cancelled bookings).
            if (!CustomerBills.IsClosedBooking(result.BookingStatusCode, result.BookingStatusLabel)
                && (await SuspensionsOfAsync(new[] { result.BookingId })).TryGetValue(result.BookingId, out var suspension))
            {
                result.AccessSuspended = true;
                result.AccessSuspendedReason = CustomerBills.SuspensionMessage(suspension.InvoiceNumber, suspension.InvoiceDueOn, result.ChallanNumber);
                result.AccessSuspendedInvoiceId = suspension.InvoiceId;
                result.AccessSuspendedInvoiceNumber = suspension.InvoiceNumber;
            }

            return ApiResponse.Ok(result);
        }

        /// <summary>
        /// Next bill due / balance left / total contract are trustworthy only once WN_vw_BookingSummary has the
        /// QA_02 billing rules (it then returns BillingRulesVersion >= 2) and the booking's bills are known
        /// (BillingFiguresKnown: false for a confirmed booking that has no invoice to bill from).
        /// </summary>
        public static bool BillingFiguresReliable(IDictionary<string, object?>? summaryRow)
        {
            if (summaryRow is null) return false;
            if (!summaryRow.TryGetValue("BillingRulesVersion", out var version) || version is null || version is DBNull) return false;
            if (Convert.ToInt32(version) < 2) return false;
            return !summaryRow.TryGetValue("BillingFiguresKnown", out var known) || known is null || known is DBNull || Convert.ToBoolean(known);
        }

        private static DateTime? ParseDateSafely(object? value)
        {
            if (value == null) return null;
            if (value is DateTime dt) return dt;
            if (value is DateOnly dOnly) return dOnly.ToDateTime(TimeOnly.MinValue);
            if (DateTime.TryParse(value.ToString(), out var parsed)) return parsed;
            return null;
        }

        /// <summary>
        /// WN_Challans.IssuedOn defaults to SYSUTCDATETIME() (UTC), while ValidUntil is a Pakistan date: the issue time is
        /// shown in Pakistan time so both dates agree (a challan made after 7 PM was showing the day before). A value with
        /// no time of day is a date set on purpose (e.g. the signed agreement's date) and is kept.
        /// </summary>
        private DateTime? ChallanIssuedOnPakistan(DateTime? issuedOn) =>
            issuedOn is DateTime d && d.TimeOfDay != TimeSpan.Zero ? _clock.FromUtc(d) : issuedOn;

        public async Task<ApiResponse> GetChallanAsync(int bookingId)
        {
            var (header, lines) = await _db.GetChallanWithDetailsAsync(bookingId);
            if (header == null)
                return ApiResponse.Fail("Challan not found");

            return ApiResponse.Ok(await BuildChallanAsync(header, lines));
        }

        /// <summary>The challan (and its totalPayable) from a WN_Challans_GetFullByBooking header + lines.</summary>
        private async Task<ChallanResponseDto> BuildChallanAsync(IDictionary<string, object?> header, IEnumerable<IDictionary<string, object?>> lines)
        {
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

            // WithholdingTaxRate may hold a WN_WHTaxRate Id (new) or a percentage (old): the challan shows the percentage.
            var wtrRaw = header.TryGetValue("WHTaxId", out var wtrTaxId) && wtrTaxId is not null && wtrTaxId is not DBNull ? wtrTaxId
                       : header.TryGetValue("WHTRate_ID", out var wtrNew) && wtrNew is not null && wtrNew is not DBNull ? wtrNew
                       : header.TryGetValue("WithholdingTaxRate", out var wtrOld) ? wtrOld : null;   // WHTaxId / WHTRate_ID / old column name
            decimal? challanWhtRaw = wtrRaw is not null && wtrRaw is not DBNull ? Convert.ToDecimal(wtrRaw) : null;
            decimal challanWhtPercent = challanWhtRaw is > 0 ? await _db.ResolveWhtRatePercentAsync(challanWhtRaw) : 15.00m;

            var dto = new ChallanResponseDto
            {
                ChallanId = Convert.ToInt32(header["ChallanId"]),
                ChallanPublicId = header["ChallanPublicId"]?.ToString(),
                ChallanNumber = header["ChallanNumber"]?.ToString() ?? "",
                IssuedOn = ChallanIssuedOnPakistan(ParseDateSafely(header["IssuedOn"])),
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
                CustomerCompany = header.TryGetValue("CustomerCompany", out var ccVal) && ccVal != null && ccVal is not DBNull ? ccVal.ToString() : null,
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
                WithholdingTaxRate = challanWhtPercent,
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
            return dto;
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

            try
            {
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
            }
            catch (Exception ex)
            {
                return ApiResponse.Fail($"The challan could not be emailed: {ex.Message}");
            }

            return ApiResponse.Ok("Challan email sent successfully.");
        }

        public async Task<byte[]> GenerateAdvanceInvoicePdfAsync(int bookingId, int advanceMonths, int? secDepositMonths, decimal monthlyRate, decimal discountAmount)
        {
            var challanResult = await GetChallanAsync(bookingId);
            var dto = challanResult.IsSuccessful ? (ChallanResponseDto)challanResult.Data! : null;

            var start = dto?.StartOn ?? _clock.Today;
            var months = new List<AdvanceInvoiceMonthDto>();
            decimal advTotal = 0m;

            if (start.Day > 1 && monthlyRate > 0)
            {
                int startDay = start.Day;
                // 30-day calendar, same as the invoice: from the day after the start day (signed on the 9th = 21/30).
                int daysInMonth = InvoiceCalculationEngine.ProrationDaysPerMonth;
                int remainingDays = daysInMonth - Math.Min(startDay, daysInMonth);
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

            // Security deposit (never prorated, SecurityDepositRules): the booking's stored months and stored deposit;
            // months asked for explicitly (0-3) are worked out with the rent's discount. No default months.
            var depositKind = SecurityDepositRules.KindOf(dto?.SpaceType, dto?.SpaceTypeName);
            int storedMonths = 0;
            var summaryRow = await _db.GetBookingSummaryRowDbAsync(bookingId);
            if (summaryRow is not null && summaryRow.TryGetValue("SecurityDepositMonths", out var sdmVal) && sdmVal is not null && sdmVal is not DBNull)
                storedMonths = Convert.ToInt32(sdmVal);
            decimal storedDeposit = dto?.SecurityDeposit ?? 0m;
            decimal depositDiscountPct = (dto?.DiscountPercentage > 0)
                ? dto.DiscountPercentage
                : ((monthlyRate > 0 && discountAmount > 0 && advanceMonths > 0)
                    ? Math.Min(100m, (discountAmount / (monthlyRate * advanceMonths)) * 100m)
                    : 0m);
            int secMonthsShown;
            decimal secTotal;
            if (secDepositMonths is null || secDepositMonths.Value == storedMonths)
            {
                secTotal = storedDeposit > 0 ? storedDeposit : 0m;
                secMonthsShown = secTotal > 0 ? storedMonths : 0;
            }
            else
            {
                secMonthsShown = depositKind != DepositSpaceKind.MeetingRoom
                    ? Math.Clamp(secDepositMonths.Value, SecurityDepositRules.MinMonths, SecurityDepositRules.MaxMonths)
                    : 0;
                secTotal = SecurityDepositRules.AmountForPercent(secMonthsShown, monthlyRate, depositDiscountPct);
                if (secTotal <= 0) secMonthsShown = 0;
            }

            // Task 2: Support charge and PST
            int capacity = dto?.SpaceCapacity > 0 ? dto.SpaceCapacity : 1;
            decimal perSeatSupportRate = 2000.00m;
            decimal taxPercentage = dto?.AppliedTaxPercentage >= 0 ? dto.AppliedTaxPercentage : 16.00m;
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
                SecurityDepositMonths = secMonthsShown,
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
                pstRate = dto.AppliedTaxPercentage >= 0 ? dto.AppliedTaxPercentage : 16.00m,
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