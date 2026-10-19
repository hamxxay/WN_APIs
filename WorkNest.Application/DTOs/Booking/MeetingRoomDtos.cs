namespace WorkNest.Application.DTOs.Booking
{
    /// <summary>POST api/booking/meeting-room/book: book a meeting room with the monthly free hours.</summary>
    public class FreeMeetingRoomBookRequest
    {
        public int SpaceId { get; set; }

        /// <summary>
        /// Start, Pakistan wall-clock time ("2026-10-20T10:00:00", as every booking StartOn). A value with "Z" or an
        /// offset is converted to Pakistan time.
        /// </summary>
        public string? StartOn { get; set; }

        /// <summary>1, 2 or 3 (dbo.WN_Bookings_InsertFreeMeetingRoom).</summary>
        public int DurationHours { get; set; }

        public string? Notes { get; set; }
    }

    /// <summary>POST api/booking/meeting-room/{id}/cancel.</summary>
    public class FreeMeetingRoomCancelRequest
    {
        public string? CancelReason { get; set; }
    }

    /// <summary>
    /// GET api/booking/meeting-room/entitlement: the free meeting-room allowance for the calendar month of a date
    /// (dbo.WN_MeetingRooms_GetEntitlement). HasEntitlement = false: no confirmed private office covers that date.
    /// </summary>
    public class MeetingRoomEntitlementDto
    {
        public bool HasEntitlement { get; set; }
        /// <summary>The date asked about (Pakistan), yyyy-MM-dd.</summary>
        public string Date { get; set; } = string.Empty;
        public int? EntitlementId { get; set; }
        public Guid? EntitlementPublicId { get; set; }
        /// <summary>Entitlement row status (WN_BookingMeetingRoomEntitlements.Status, "Active" = usable).</summary>
        public string? Status { get; set; }
        public string? MonthStart { get; set; }
        public string? MonthEnd { get; set; }
        public int AllocatedMinutes { get; set; }
        public int UsedMinutes { get; set; }
        public int RemainingMinutes { get; set; }
        /// <summary>Longest single free booking in hours (dbo.WN_Bookings_InsertFreeMeetingRoom).</summary>
        public int MaxBookingHours { get; set; }
        public int? PrivateBookingId { get; set; }
        public string? PrivateSpaceName { get; set; }
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        public DateTime? PrivateBookingEndOn { get; set; }
        /// <summary>Why there are no free hours (null when there are).</summary>
        public string? Message { get; set; }
    }

    /// <summary>GET api/booking/meeting-room/rooms: a room the customer may book with free hours on the date.</summary>
    public class MeetingRoomDto
    {
        public int SpaceId { get; set; }
        public Guid? SpacePublicId { get; set; }
        public string? SpaceCode { get; set; }
        public string SpaceName { get; set; } = string.Empty;
        public int? Capacity { get; set; }
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        /// <summary>Times the room is taken that day (any booking that blocks a free booking).</summary>
        public List<MeetingRoomBusyDto> Busy { get; set; } = new();
    }

    public class MeetingRoomBusyDto
    {
        public DateTime StartOn { get; set; }
        public DateTime EndOn { get; set; }
    }

    /// <summary>GET api/booking/meeting-room/rooms result.</summary>
    public class MeetingRoomsForDateDto
    {
        public string Date { get; set; } = string.Empty;
        public List<MeetingRoomDto> Rooms { get; set; } = new();
    }

    /// <summary>A free meeting-room booking of the customer (dbo.WN_MeetingRooms_GetMyFree).</summary>
    public class FreeMeetingRoomBookingDto
    {
        public int BookingId { get; set; }
        public Guid? BookingPublicId { get; set; }
        public int SpaceId { get; set; }
        public string? SpaceCode { get; set; }
        public string? SpaceName { get; set; }
        public string? LocationName { get; set; }
        public DateTime StartOn { get; set; }
        public DateTime EndOn { get; set; }
        public int DurationMinutes { get; set; }
        public int BookingStatusId { get; set; }
        public string? BookingStatusCode { get; set; }
        public string? BookingStatusLabel { get; set; }
        public string? ChallanNumber { get; set; }
        public string? Notes { get; set; }
        public string? CancelReason { get; set; }
        public DateTime? CreatedOn { get; set; }
        /// <summary>Confirmed and not started yet (dbo.WN_MeetingRooms_CancelFree rule).</summary>
        public bool CanCancel { get; set; }
        public bool IsPast { get; set; }
    }

    /// <summary>POST api/booking/meeting-room/book result.</summary>
    public class FreeMeetingRoomBookedDto
    {
        public int BookingId { get; set; }
        public Guid? BookingPublicId { get; set; }
        public string? ChallanNumber { get; set; }
        public int SpaceId { get; set; }
        public DateTime StartOn { get; set; }
        public DateTime EndOn { get; set; }
        public int DurationHours { get; set; }
    }

    /// <summary>Result of a meeting-room call: HTTP status, message and data (ApiResponse body).</summary>
    public class MeetingRoomOutcome
    {
        public int StatusCode { get; set; } = 200;
        public string Message { get; set; } = string.Empty;
        public object? Data { get; set; }
        public bool IsSuccessful => StatusCode is >= 200 and < 300;
    }
}
