using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class BookingController : ControllerBase
    {
        private readonly IBookingService _bookings;
        public BookingController(IBookingService bookings) => _bookings = bookings;

        [HttpGet("api/booking/{id:int}/account")]
        public async Task<IActionResult> GetBookingAccount(int id)
        {
            var result = await _bookings.GetBookingAccountAsync(id.ToString());
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("api/booking/available-spaces")]
        [AllowAnonymous]
        public async Task<IActionResult> AvailableSpaces(
            [FromQuery] string spaceType,
            [FromQuery] string startDateTime,
            [FromQuery] string endDateTime) =>
            Ok(await _bookings.GetAvailableSpacesAsync(spaceType, startDateTime, endDateTime));

        [HttpGet("api/booking/available-spaces-reassignment")]
        [AllowAnonymous]
        public async Task<IActionResult> AvailableForReassignment(
            [FromQuery] string spaceType,
            [FromQuery] string startDateTime,
            [FromQuery] string endDateTime,
            [FromQuery] int? excludeBookingId) =>
            Ok(await _bookings.GetAvailableSpacesForReassignmentAsync(spaceType, startDateTime, endDateTime, excludeBookingId));

        [HttpGet("api/booking/my")]
        public async Task<IActionResult> MyBookings([FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "User email header required" });
            return Ok(await _bookings.GetMyBookingsAsync(userEmail));
        }

        [HttpGet("api/booking/recent")]
        public async Task<IActionResult> Recent([FromQuery] int limit = 5)
        {
            var all = await _bookings.GetAllBookingsAsync();
            return Ok(ApiResponse.Ok(all.Take(limit)));
        }

        [HttpGet("api/booking/calendar")]
        public async Task<IActionResult> Calendar(
            [FromQuery] int spaceId,
            [FromQuery] int year,
            [FromQuery] int month) =>
            Ok(await _bookings.GetBookingCalendarAsync(spaceId, year, month));

        [HttpGet("api/booking/smart/available")]
        [AllowAnonymous]
        public async Task<IActionResult> SmartAvailable(
            [FromQuery] string spaceCategory,
            [FromQuery] string startDateTime,
            [FromQuery] string endDateTime,
            [FromQuery] int? capacity) =>
            Ok(await _bookings.GetSmartAvailableSpacesAsync(spaceCategory, startDateTime, endDateTime, capacity));

        [HttpGet("api/booking")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string search = "")
        {
            var all = await _bookings.GetAllBookingsAsync();
            var (items, total) = PaginationHelper.Paginate(all, page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpGet("api/booking/{id:int}")]
        public async Task<IActionResult> Get(int id, [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "User email header required" });
            var result = await _bookings.GetBookingByIdAsync(id.ToString(), userEmail);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/booking/create-admin")]
        [Authorize(Roles = "admin,super_admin,receptionist")]
        public async Task<IActionResult> AdminCreate([FromBody] AdminBookingRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SpaceId) || string.IsNullOrWhiteSpace(request.StartDateTime) || string.IsNullOrWhiteSpace(request.EndDateTime))
                return BadRequest(new { isSuccessful = false, message = "SpaceId, StartDateTime and EndDateTime are required" });
            if (string.IsNullOrWhiteSpace(request.UserId) && string.IsNullOrWhiteSpace(request.CustomerEmail))
                return BadRequest(new { isSuccessful = false, message = "UserId or CustomerEmail is required" });
            return StatusCode(201, await _bookings.CreateAdminBookingAsync(request));
        }

        [HttpPost("api/booking")]
        public async Task<IActionResult> Create(
            [FromBody] BookingRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "User email header required" });
            return StatusCode(201, await _bookings.CreateBookingAsync(request, userEmail));
        }

        [HttpPost("api/booking/smart")]
        public async Task<IActionResult> Smart(
            [FromBody] SmartBookingRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "User email header required" });
            return StatusCode(201, await _bookings.CreateSmartBookingAsync(request, userEmail));
        }

        [HttpPatch("api/booking/{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "User email header required" });
            var result = await _bookings.CancelBookingAsync(id.ToString(), userEmail);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPatch("api/booking/{id:int}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromQuery] string status) =>
            Ok(await _bookings.UpdateBookingStatusAsync(id.ToString(), status));

        [HttpPatch("api/booking/{id:int}/reassign")]
        public async Task<IActionResult> Reassign(
            int id,
            [FromBody] ReassignBookingRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "Admin email header required" });
            var result = await _bookings.ReassignBookingAsync(id.ToString(), request, userEmail);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPut("api/booking/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] BookingRequest request) =>
            Ok(await _bookings.UpdateBookingAsync(id.ToString(), request));
    }
}
