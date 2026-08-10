using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class BookingController : ControllerBase
    {
        private readonly IBookingService _bookings;
        private readonly IDbRepository _db;
        public BookingController(IBookingService bookings, IDbRepository db)
        {
            _bookings = bookings;
            _db = db;
        }

        [HttpGet("api/booking/available-spaces")]
        [AllowAnonymous]
        public async Task<IActionResult> AvailableSpaces(
            [FromQuery] int spaceTypeId,
            [FromQuery] DateTime startOn,
            [FromQuery] DateTime endOn,
            [FromQuery] int? capacity) =>
            Ok(await _bookings.GetAvailableSpacesForBookingAsync(spaceTypeId, startOn, endOn, capacity));

        [HttpGet("api/booking/available-spaces-reassignment")]
        [AllowAnonymous]
        public async Task<IActionResult> AvailableForReassignment(
            [FromQuery] int spaceTypeId,
            [FromQuery] DateTime startOn,
            [FromQuery] DateTime endOn,
            [FromQuery] int excludeBookingId) =>
            Ok(await _bookings.GetAvailableSpacesForReassignmentAsync(spaceTypeId, startOn, endOn, excludeBookingId));

        [HttpGet("api/booking/smart/available")]
        [AllowAnonymous]
        public async Task<IActionResult> SmartAvailable(
            [FromQuery] string categoryCode,
            [FromQuery] DateTime startOn,
            [FromQuery] DateTime endOn,
            [FromQuery] int? capacity) =>
            Ok(await _bookings.GetSmartAvailableSpacesAsync(categoryCode, startOn, endOn, capacity));

        [HttpGet("api/booking/my")]
        public async Task<IActionResult> MyBookings([FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "User email header required" });
            return Ok(await _bookings.GetMyBookingsAsync(userEmail));
        }

        [HttpGet("api/booking/recent")]
        public async Task<IActionResult> Recent([FromQuery] int top = 10) =>
            Ok(ApiResponse.Ok(await _bookings.GetRecentBookingsAsync(top)));

        [HttpGet("api/booking/calendar")]
        public async Task<IActionResult> Calendar(
            [FromQuery] string spaceId,
            [FromQuery] int year,
            [FromQuery] int month)
        {
            // Accept both int and GUID for spaceId
            if (int.TryParse(spaceId, out var intId))
                return Ok(await _bookings.GetBookingCalendarAsync(intId, year, month));

            if (Guid.TryParse(spaceId, out var guid))
            {
                var (rows, _) = await _db.GetSpacesAsync(1, 10000, null);
                var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == guid.ToString());
                if (match is null) return NotFound(new { isSuccessful = false, message = "Space not found" });
                intId = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
                return Ok(await _bookings.GetBookingCalendarAsync(intId, year, month));
            }

            return BadRequest(new { isSuccessful = false, message = "Invalid spaceId" });
        }

        [HttpGet("api/booking")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null)
        {
            var (items, total) = await _bookings.GetBookingsAsync(page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpGet("api/booking/{publicId:guid}")]
        public async Task<IActionResult> Get(Guid publicId, [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            var result = await _bookings.GetBookingByIdAsync(publicId, userEmail);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/booking/create-admin")]
        [Authorize(Roles = "admin,super_admin,receptionist")]
        public async Task<IActionResult> AdminCreate(
            [FromBody] AdminBookingRequest request,
            [FromHeader(Name = "x-user-email")] string? actorEmail)
        {
            if (request.SpaceId == 0)
                return BadRequest(new { isSuccessful = false, message = "SpaceId is required" });
            if (request.UserId == 0 && string.IsNullOrWhiteSpace(request.CustomerEmail))
                return BadRequest(new { isSuccessful = false, message = "UserId or CustomerEmail is required" });
            return StatusCode(201, await _bookings.CreateAdminBookingAsync(request, actorEmail));
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

        [HttpPut("api/booking/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] BookingUpdateRequest request) =>
            Ok(await _bookings.UpdateBookingAsync(id, request, null));

        [HttpPut("api/booking/{publicId:guid}")]
        public async Task<IActionResult> UpdateByGuid(Guid publicId, [FromBody] BookingUpdateRequest request)
        {
            var booking = await _db.GetBookingByPublicIdAsync(publicId, null);
            if (booking is null) return NotFound(new { isSuccessful = false, message = "Booking not found" });
            var id = booking.TryGetValue("Id", out var bid) ? Convert.ToInt32(bid) : 0;
            return Ok(await _bookings.UpdateBookingAsync(id, request, null));
        }

        [HttpPatch("api/booking/{id:int}/cancel")]
        public async Task<IActionResult> Cancel(
            int id,
            [FromBody] CancelBookingRequest? request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "User email header required" });
            var result = await _bookings.CancelBookingAsync(id, userEmail, request?.CancelReason);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPatch("api/booking/{publicId:guid}/cancel")]
        public async Task<IActionResult> CancelByGuid(
            Guid publicId,
            [FromBody] CancelBookingRequest? request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "User email header required" });
            var booking = await _db.GetBookingByPublicIdAsync(publicId, userEmail);
            if (booking is null) return NotFound(new { isSuccessful = false, message = "Booking not found" });
            var id = booking.TryGetValue("Id", out var bid) ? Convert.ToInt32(bid) : 0;
            var result = await _bookings.CancelBookingAsync(id, userEmail, request?.CancelReason);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPatch("api/booking/{id:int}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] BookingStatusUpdateRequest request) =>
            Ok(await _bookings.UpdateBookingStatusAsync(id, request.StatusId, null));

        [HttpPatch("api/booking/{publicId:guid}/status")]
        public async Task<IActionResult> UpdateStatusByGuid(Guid publicId, [FromBody] BookingStatusUpdateRequest request)
        {
            var booking = await _db.GetBookingByPublicIdAsync(publicId, null);
            if (booking is null) return NotFound(new { isSuccessful = false, message = "Booking not found" });
            var id = booking.TryGetValue("Id", out var bid) ? Convert.ToInt32(bid) : 0;
            return Ok(await _bookings.UpdateBookingStatusAsync(id, request.StatusId, null));
        }

        [HttpPatch("api/booking/{id:int}/reassign")]
        public async Task<IActionResult> Reassign(
            int id,
            [FromBody] ReassignBookingRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "Admin email header required" });
            var result = await _bookings.ReassignBookingAsync(id, request, userEmail);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPatch("api/booking/{publicId:guid}/reassign")]
        public async Task<IActionResult> ReassignByGuid(
            Guid publicId,
            [FromBody] ReassignBookingRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "Admin email header required" });
            var booking = await _db.GetBookingByPublicIdAsync(publicId, null);
            if (booking is null) return NotFound(new { isSuccessful = false, message = "Booking not found" });
            var id = booking.TryGetValue("Id", out var bid) ? Convert.ToInt32(bid) : 0;
            var result = await _bookings.ReassignBookingAsync(id, request, userEmail);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }
    }
}
