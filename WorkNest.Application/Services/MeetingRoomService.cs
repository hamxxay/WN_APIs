using System.Globalization;
using System.Text.RegularExpressions;
using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Free meeting-room hours of the signed-in customer (api/booking/meeting-room/*). The rules live in the database:
    /// dbo.WN_MeetingRooms_GetEntitlement (which private office booking gives the hours; creates the month allowance row
    /// with dbo.WN_BookingMeetingRoomEntitlements_Generate when missing), dbo.WN_MeetingRooms_GetRooms and
    /// dbo.WN_MeetingRooms_BookFree (only rooms at the location of that same office, the one whose hours are charged:
    /// a customer with offices at two locations books at the earliest-starting one; start in the future; then
    /// dbo.WN_Bookings_InsertFreeMeetingRoom: 1-3 hours, remaining hours, no overlap), dbo.WN_MeetingRooms_CancelFree
    /// (own, confirmed, not started; then dbo.WN_Bookings_CancelFreeMeetingRoom). This class only checks the shape of
    /// the input and maps the rows.
    /// </summary>
    public class MeetingRoomService : IMeetingRoomService
    {
        public const int NotesMaxLength = 500;
        public const int CancelReasonMaxLength = 400; // dbo.WN_MeetingRooms_CancelFree @CancelReason

        public const string NoEntitlementMessage =
            "Free meeting-room hours come with a confirmed private office booking. None of your private office bookings covers this date.";

        private readonly IDbRepository _db;
        private readonly IBusinessClock _clock;

        public MeetingRoomService(IDbRepository db, IBusinessClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<int?> ResolveUserIdAsync(string? email, string? idClaim)
        {
            if (!string.IsNullOrWhiteSpace(email))
            {
                var (id, _) = await _db.GetUserIdByEmailAsync(email);
                if (id is > 0) return id;
            }
            return int.TryParse(idClaim, out var n) && n > 0 ? n : null;
        }

        public async Task<MeetingRoomOutcome> GetEntitlementAsync(int userId, string? date)
        {
            if (!TryParseDay(date, _clock, out var day)) return Bad("Date must be in the format YYYY-MM-DD.");

            var row = await _db.GetMeetingRoomEntitlementDbAsync(userId, day);
            var dto = new MeetingRoomEntitlementDto { Date = Day(day) };
            if (row is null)
            {
                dto.Message = NoEntitlementMessage;
                return Ok(dto);
            }

            dto.EntitlementId = Int(row, "EntitlementId");
            dto.EntitlementPublicId = GuidOf(row, "EntitlementPublicId");
            dto.Status = Text(row, "Status");
            dto.MonthStart = DateOf(row, "MonthStartDate") is { } ms ? Day(ms) : null;
            dto.MonthEnd = DateOf(row, "MonthEndDate") is { } me ? Day(me) : null;
            dto.AllocatedMinutes = Int(row, "AllocatedMinutes") ?? 0;
            dto.UsedMinutes = Int(row, "UsedMinutes") ?? 0;
            dto.RemainingMinutes = Math.Max(0, Int(row, "RemainingMinutes") ?? 0);
            dto.MaxBookingHours = Int(row, "MaxBookingHours") ?? 0;
            dto.PrivateBookingId = Int(row, "PrivateBookingId");
            dto.PrivateSpaceName = Text(row, "PrivateSpaceName");
            dto.LocationId = Int(row, "LocationId");
            dto.LocationName = Text(row, "LocationName");
            dto.PrivateBookingEndOn = DateOf(row, "PrivateBookingEndOn");
            // The booking procedure only uses an "Active" allowance row.
            dto.HasEntitlement = string.Equals(dto.Status, "Active", StringComparison.OrdinalIgnoreCase);
            if (!dto.HasEntitlement)
                dto.Message = "Your free meeting-room hours for this month are on hold. Please contact WorkNest.";
            return Ok(dto);
        }

        public async Task<MeetingRoomOutcome> GetRoomsAsync(int userId, string? date)
        {
            if (!TryParseDay(date, _clock, out var day)) return Bad("Date must be in the format YYYY-MM-DD.");

            var (rooms, busy) = await _db.GetMeetingRoomsForFreeHoursDbAsync(userId, day);
            var list = rooms.Select(r => new MeetingRoomDto
            {
                SpaceId = Int(r, "SpaceId") ?? 0,
                SpacePublicId = GuidOf(r, "SpacePublicId"),
                SpaceCode = Text(r, "SpaceCode"),
                SpaceName = Text(r, "SpaceName") ?? Text(r, "SpaceCode") ?? "Meeting room",
                Capacity = Int(r, "Capacity"),
                LocationId = Int(r, "LocationId"),
                LocationName = Text(r, "LocationName")
            }).Where(r => r.SpaceId > 0).ToList();

            var bySpace = list.ToDictionary(r => r.SpaceId);
            foreach (var b in busy)
            {
                var start = DateOf(b, "StartOn");
                var end = DateOf(b, "EndOn");
                if (start is null || end is null) continue;
                if (bySpace.TryGetValue(Int(b, "SpaceId") ?? 0, out var room))
                    room.Busy.Add(new MeetingRoomBusyDto { StartOn = start.Value, EndOn = end.Value });
            }
            return Ok(new MeetingRoomsForDateDto { Date = Day(day), Rooms = list });
        }

        public async Task<MeetingRoomOutcome> BookAsync(int userId, FreeMeetingRoomBookRequest? request)
        {
            if (request is null) return Bad("Booking details are required.");
            if (request.SpaceId <= 0) return Bad("Choose a meeting room.");
            var start = ParseStartOn(request.StartOn, _clock);
            if (start is null) return Bad("Choose a valid start date and time.");
            var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            if (notes is { Length: > NotesMaxLength }) return Bad($"Notes can be at most {NotesMaxLength} characters.");

            // Duration (1-3 hours), remaining hours, overlap, start in the future and location: the procedures.
            var row = await _db.BookFreeMeetingRoomDbAsync(userId, request.SpaceId, start.Value, request.DurationHours, notes);
            var error = Text(row, "ErrorMessage");
            if (error is not null) return Bad(error);
            var bookingId = Int(row, "BookingId");
            if (bookingId is not > 0) return Bad("The meeting room could not be booked. Please try again.");

            return new MeetingRoomOutcome
            {
                StatusCode = 201,
                Message = "Meeting room booked with your free hours.",
                Data = new FreeMeetingRoomBookedDto
                {
                    BookingId = bookingId.Value,
                    BookingPublicId = GuidOf(row, "BookingPublicId"),
                    ChallanNumber = Text(row, "ChallanNumber"),
                    SpaceId = request.SpaceId,
                    StartOn = start.Value,
                    EndOn = start.Value.AddHours(request.DurationHours),
                    DurationHours = request.DurationHours
                }
            };
        }

        public async Task<MeetingRoomOutcome> CancelAsync(int userId, int bookingId, FreeMeetingRoomCancelRequest? request)
        {
            if (bookingId <= 0) return NotFound("Free meeting-room booking not found.");
            var reason = string.IsNullOrWhiteSpace(request?.CancelReason) ? null : request!.CancelReason!.Trim();
            if (reason is { Length: > CancelReasonMaxLength }) return Bad($"The reason can be at most {CancelReasonMaxLength} characters.");

            var row = await _db.CancelFreeMeetingRoomDbAsync(bookingId, userId, reason);
            if (Bool(row, "NotFound")) return NotFound(Text(row, "ErrorMessage") ?? "Free meeting-room booking not found.");
            if (!Bool(row, "Cancelled")) return Bad(Text(row, "ErrorMessage") ?? "The booking could not be cancelled. Please try again.");
            return Ok(new { bookingId }, "Booking cancelled. The hours are back in your monthly allowance.");
        }

        public async Task<MeetingRoomOutcome> GetMyAsync(int userId)
        {
            var rows = await _db.GetMyFreeMeetingRoomsDbAsync(userId);
            var list = new List<FreeMeetingRoomBookingDto>();
            foreach (var r in rows)
            {
                var start = DateOf(r, "StartOn");
                var end = DateOf(r, "EndOn");
                if (start is null || end is null) continue;
                list.Add(new FreeMeetingRoomBookingDto
                {
                    BookingId = Int(r, "BookingId") ?? 0,
                    BookingPublicId = GuidOf(r, "BookingPublicId"),
                    SpaceId = Int(r, "SpaceId") ?? 0,
                    SpaceCode = Text(r, "SpaceCode"),
                    SpaceName = Text(r, "SpaceName"),
                    LocationName = Text(r, "LocationName"),
                    StartOn = start.Value,
                    EndOn = end.Value,
                    DurationMinutes = Int(r, "DurationMinutes") ?? (int)(end.Value - start.Value).TotalMinutes,
                    BookingStatusId = Int(r, "BookingStatusId") ?? 0,
                    BookingStatusCode = Text(r, "BookingStatusCode"),
                    BookingStatusLabel = Text(r, "BookingStatusLabel"),
                    ChallanNumber = Text(r, "ChallanNumber"),
                    Notes = Text(r, "Notes"),
                    CancelReason = Text(r, "CancelReason"),
                    CreatedOn = DateOf(r, "CreatedOn"),
                    CanCancel = Bool(r, "CanCancel"),
                    IsPast = Bool(r, "IsPast")
                });
            }
            return Ok(list);
        }

        // ---- input ---------------------------------------------------------------------------------------------

        private static readonly Regex HasOffset = new(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Start as Pakistan wall-clock time (how every booking StartOn is stored). Without "Z" / an offset the text is
        /// already Pakistan time; with one it is converted. Null when it is not a date and time.
        /// </summary>
        public static DateTime? ParseStartOn(string? text, IBusinessClock clock)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var s = text.Trim();
            if (HasOffset.IsMatch(s))
            {
                return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)
                    ? DateTime.SpecifyKind(clock.FromUtc(dto.UtcDateTime), DateTimeKind.Unspecified)
                    : null;
            }
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
                ? DateTime.SpecifyKind(local, DateTimeKind.Unspecified)
                : null;
        }

        /// <summary>yyyy-MM-dd; empty = today (Pakistan).</summary>
        public static bool TryParseDay(string? text, IBusinessClock clock, out DateTime day)
        {
            if (string.IsNullOrWhiteSpace(text)) { day = clock.Today; return true; }
            return DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
        }

        // ---- rows ----------------------------------------------------------------------------------------------

        private static string Day(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static object? Value(IDictionary<string, object?> row, string key) =>
            row.TryGetValue(key, out var v) && v is not null && v is not DBNull ? v : null;

        private static string? Text(IDictionary<string, object?> row, string key) =>
            Value(row, key)?.ToString() is { } s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

        private static int? Int(IDictionary<string, object?> row, string key) =>
            Value(row, key) is { } v && int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var n) ? n : null;

        private static bool Bool(IDictionary<string, object?> row, string key) =>
            Value(row, key) switch
            {
                bool b => b,
                null => false,
                var v => Convert.ToString(v, CultureInfo.InvariantCulture) is "1" or "True" or "true"
            };

        private static DateTime? DateOf(IDictionary<string, object?> row, string key) =>
            Value(row, key) switch
            {
                DateTime d => d,
                DateTimeOffset o => o.DateTime,
                _ => null
            };

        private static Guid? GuidOf(IDictionary<string, object?> row, string key) =>
            Value(row, key) switch
            {
                Guid g => g,
                var v when v is not null && Guid.TryParse(v.ToString(), out var g) => g,
                _ => null
            };

        private static MeetingRoomOutcome Ok(object? data, string message = "Success") => new() { StatusCode = 200, Message = message, Data = data };
        private static MeetingRoomOutcome Bad(string message) => new() { StatusCode = 400, Message = message };
        private static MeetingRoomOutcome NotFound(string message) => new() { StatusCode = 404, Message = message };
    }
}
