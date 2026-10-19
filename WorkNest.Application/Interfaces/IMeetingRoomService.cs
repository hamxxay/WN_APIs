using WorkNest.Application.DTOs.Booking;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Free meeting-room hours (page My Meeting Rooms, api/booking/meeting-room/*): a customer with a confirmed private
    /// office booking gets a monthly allowance (dbo.WN_BookingMeetingRoomEntitlements) and books meeting rooms at that
    /// location with it. Always the signed-in user's own allowance and bookings. Through the dbo.WN_MeetingRooms_*
    /// procedures (script MR_01_meeting_rooms); before that script is run every call throws
    /// <see cref="MeetingRoomsNotInstalledException"/>.
    /// </summary>
    public interface IMeetingRoomService
    {
        /// <summary>The signed-in user's WN_Users.Id (by email; older tokens carried the numeric id).</summary>
        Task<int?> ResolveUserIdAsync(string? email, string? idClaim);

        /// <summary>The allowance for the month of <paramref name="date"/> (yyyy-MM-dd; empty = today, Pakistan).</summary>
        Task<MeetingRoomOutcome> GetEntitlementAsync(int userId, string? date);

        /// <summary>Meeting rooms the user may book with free hours on <paramref name="date"/>, with their taken times.</summary>
        Task<MeetingRoomOutcome> GetRoomsAsync(int userId, string? date);

        Task<MeetingRoomOutcome> BookAsync(int userId, FreeMeetingRoomBookRequest? request);

        Task<MeetingRoomOutcome> CancelAsync(int userId, int bookingId, FreeMeetingRoomCancelRequest? request);

        /// <summary>The user's free meeting-room bookings, newest first.</summary>
        Task<MeetingRoomOutcome> GetMyAsync(int userId);
    }

    /// <summary>
    /// The dbo.WN_MeetingRooms_* procedures (or a table / procedure they use) are not on the database yet: SQL errors
    /// 2812 / 208. The page shows <see cref="UserMessage"/> instead of an error (no ".sql" in it: the web error filter
    /// hides any text that looks like SQL).
    /// </summary>
    public class MeetingRoomsNotInstalledException : Exception
    {
        public const string UserMessage =
            "Database update needed: run the script MR_01_meeting_rooms_TEST (MR_01_meeting_rooms_LIVE on the live database).";

        public MeetingRoomsNotInstalledException(Exception inner) : base(UserMessage, inner) { }
    }
}
