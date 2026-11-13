namespace WorkNest.Application.DTOs.Reception
{
    // Front desk (Receptionist role, api/reception/*). Serialized camelCase inside the { isSuccessful, message, data } envelope.
    // Times of meeting rooms are Pakistan wall-clock time (as every booking StartOn); tour dates are plain dates.

    /// <summary>POST api/reception/tour-inquiries: a tour inquiry added by staff (walk-in, phone call, ...).</summary>
    public class TourInquiryRequest
    {
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Company { get; set; }
        /// <summary>yyyy-MM-dd</summary>
        public string? PreferredDate { get; set; }
        /// <summary>Free text such as "11:00" or "afternoon" (at most 20 characters).</summary>
        public string? PreferredTime { get; set; }
        /// <summary>The branch (dbo.WN_Locations.Id); required. Location-bound staff: one of their own locations (403 otherwise).</summary>
        public int LocationId { get; set; }
        public string? SpaceType { get; set; }
        public string? Notes { get; set; }
        /// <summary>walk_in | phone | other</summary>
        public string? Source { get; set; }
    }

    /// <summary>POST api/reception/meeting-rooms/book</summary>
    public class ReceptionMeetingBookRequest
    {
        public int CustomerId { get; set; }
        public int SpaceId { get; set; }
        /// <summary>Pakistan wall-clock ("2026-11-20T10:00:00"); a value with Z / an offset is converted.</summary>
        public string? Start { get; set; }
        public string? End { get; set; }
    }

    /// <summary>POST api/reception/network/speedtest</summary>
    public class ReceptionSpeedtestRequest
    {
        public int LocationId { get; set; }
        /// <summary>Optional line label (WAN1 / WAN2); omitted = the Primary line.</summary>
        public string? Wan { get; set; }
    }

    // ---- Today ----

    public sealed record TodayTourDto(int Id, Guid? PublicId, string? Name, string? Phone, string? Email, string? PreferredDate,
        string? PreferredTime, string Status, string? LocationName);

    public sealed record TodayMeetingDto(int Id, string? RoomName, string? CustomerName, string Start, string End, string Status,
        string? CheckedInAt, string? CheckedOutAt);

    public sealed record TodayComplaintDto(int Id, string Title, string? CustomerName, string Status, string? CreatedAt, string? LocationName);

    public sealed record TodayInvoiceDto(int InvoiceId, string? InvoiceNumber, string? CustomerName, string? DueDate, decimal Balance);

    public sealed record TodayCountsDto(int Tours, int MeetingRooms, int OpenComplaints, int InvoicesDue);

    public sealed record TodayDto(List<TodayTourDto> Tours, List<TodayMeetingDto> MeetingRooms, List<TodayComplaintDto> Complaints,
        List<TodayInvoiceDto> InvoicesDueToday, TodayCountsDto Counts);

    // ---- Meeting rooms ----

    public sealed record MeetingRoomItemDto(int SpaceId, string? Name, string? LocationName, int? Capacity);

    public sealed record MeetingBookingDto(int Id, int SpaceId, int CustomerId, string? CustomerName, string Start, string End, string Status,
        string? CheckedInAt, string? CheckedOutAt, string? BookedBy);

    public sealed record MeetingCalendarDto(List<MeetingRoomItemDto> Rooms, List<MeetingBookingDto> Bookings);

    // ---- Network ----

    public sealed record ReceptionSpeedTestDto(string? At, double? DownloadMbps, double? UploadMbps, double? PingMs);

    public sealed record ReceptionNetworkDto(int LocationId, string? LocationName, bool Online, double? UptimePercent24h, string? LastDownAt,
        int ClientsNow, ReceptionSpeedTestDto? LastSpeedTest, List<ReceptionSpeedTestDto> SpeedTests, bool SpeedTestSupported,
        List<ReceptionNetworkLineDto>? Lines = null);

    /// <summary>One internet line (WAN) of a location with its own speed-test history.</summary>
    public sealed record ReceptionNetworkLineDto(string Label, string? Isp, string? Role, bool Up, bool Live, double? UptimePercent24h,
        ReceptionSpeedTestDto? LastSpeedTest, List<ReceptionSpeedTestDto> SpeedTests, bool CanTest);

    // ---- Repository rows (dbo.WN_Contacts / WN_Bookings / WN_Invoices, read by dbo-less inline SQL) ----

    public sealed record ReceptionTourRow(int Id, Guid? PublicId, string? Name, string? Phone, string? Email, DateTime? PreferredDate,
        string? PreferredTime, int StatusId, string? LocationName);

    public sealed record ReceptionRoomRow(int SpaceId, string? Name, int? LocationId, string? LocationName, int? Capacity);

    public sealed record ReceptionBookingRow(int Id, int SpaceId, string? SpaceName, int? LocationId, int CustomerId, string? CustomerName,
        DateTime Start, DateTime End, int StatusId, bool IsFree, DateTime? CheckedInAt, DateTime? CheckedOutAt, string? BookedBy);

    public sealed record ReceptionInvoiceRow(int InvoiceId, string? InvoiceNumber, string? CustomerName, DateTime? DueDate, decimal Balance);
}
