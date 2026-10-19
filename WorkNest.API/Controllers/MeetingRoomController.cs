using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Free meeting-room hours (page My Meeting Rooms): a customer with a confirmed private office booking gets a monthly
    /// allowance and books meeting rooms at that location with it. Like api/booking/my, always the signed-in user's own
    /// allowance and bookings (user from the JWT email). Procedures dbo.WN_MeetingRooms_* (script MR_01_meeting_rooms);
    /// before it is run every call answers 503 "Database update needed: run the script MR_01_meeting_rooms_TEST ...".
    /// </summary>
    [ApiController]
    [Authorize]
    public class MeetingRoomController : ControllerBase
    {
        private readonly IMeetingRoomService _rooms;

        public MeetingRoomController(IMeetingRoomService rooms)
        {
            _rooms = rooms;
        }

        /// <summary>The allowance for the calendar month of <paramref name="date"/> (yyyy-MM-dd, default today).</summary>
        [HttpGet("api/booking/meeting-room/entitlement")]
        public Task<IActionResult> Entitlement([FromQuery] string? date) =>
            RunAsync(userId => _rooms.GetEntitlementAsync(userId, date));

        /// <summary>Meeting rooms the user may book with free hours on <paramref name="date"/>, with the times taken.</summary>
        [HttpGet("api/booking/meeting-room/rooms")]
        public Task<IActionResult> Rooms([FromQuery] string? date) =>
            RunAsync(userId => _rooms.GetRoomsAsync(userId, date));

        /// <summary>Book a meeting room with the free hours (201 = booked).</summary>
        [HttpPost("api/booking/meeting-room/book")]
        public Task<IActionResult> Book([FromBody] FreeMeetingRoomBookRequest? request) =>
            RunAsync(userId => _rooms.BookAsync(userId, request));

        /// <summary>Cancel an own free booking before it starts; the hours go back to the allowance.</summary>
        [HttpPost("api/booking/meeting-room/{id:int}/cancel")]
        public Task<IActionResult> Cancel(int id, [FromBody] FreeMeetingRoomCancelRequest? request) =>
            RunAsync(userId => _rooms.CancelAsync(userId, id, request));

        /// <summary>The user's free meeting-room bookings, newest first.</summary>
        [HttpGet("api/booking/meeting-room/my")]
        public Task<IActionResult> My() =>
            RunAsync(userId => _rooms.GetMyAsync(userId));

        private async Task<IActionResult> RunAsync(Func<int, Task<MeetingRoomOutcome>> action)
        {
            try
            {
                var userId = await _rooms.ResolveUserIdAsync(User.GetEmail(), User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                if (userId is not > 0)
                    return Unauthorized(ApiResponse.Fail("User identity required"));
                return ToResult(await action(userId.Value));
            }
            catch (MeetingRoomsNotInstalledException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(MeetingRoomsNotInstalledException.UserMessage));
            }
        }

        internal static IActionResult ToResult(MeetingRoomOutcome o) =>
            new ObjectResult(new ApiResponse { IsSuccessful = o.IsSuccessful, Message = o.Message, Data = o.Data }) { StatusCode = o.StatusCode };
    }
}
