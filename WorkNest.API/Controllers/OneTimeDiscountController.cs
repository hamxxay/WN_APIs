using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.OneTimeDiscount;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize(Roles = "super_admin,SuperAdmin,admin,Admin,sales_executive,SalesExecutive")]
    [ValidateLocationScope]
    public class OneTimeDiscountController : ControllerBase
    {
        private readonly IOneTimeDiscountService _service;
        private readonly IDbRepository _db;

        public OneTimeDiscountController(IOneTimeDiscountService service, IDbRepository db)
        {
            _service = service;
            _db = db;
        }

        private string? ResolveUserEmail() => User.GetEmail();

        private async Task<int> ResolveActorIdAsync()
        {
            var idClaim = User.FindFirst("userId")?.Value
                       ?? User.FindFirst("id")?.Value
                       ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(idClaim, out int parsed) && parsed > 0)
                return parsed;

            var email = ResolveUserEmail();
            if (!string.IsNullOrWhiteSpace(email))
            {
                var userRow = await _db.GetUserByEmailAsync(email);
                if (userRow?.TryGetValue("Id", out var id) == true && id != null)
                    return Convert.ToInt32(id);
            }
            return 1;
        }

        [HttpGet("api/booking/{bookingId:int}/one-time-discounts")]
        [HttpGet("api/one-time-discounts/booking/{bookingId:int}")]
        [RecordScope(RecordKind.Booking, "bookingId")]
        public async Task<IActionResult> GetByBooking(int bookingId)
        {
            if (User.IsLocationBoundRole() && !await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Booking, bookingId.ToString()))
                return StatusCode(403, ApiResponse.Fail("Access denied: booking belongs to another location."));

            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            var result = await _service.GetByBookingAsync(bookingId, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        [HttpPost("api/booking/{bookingId:int}/one-time-discounts/preview")]
        [RecordScope(RecordKind.Booking, "bookingId")]
        public async Task<IActionResult> PreviewForBooking(int bookingId, [FromBody] OneTimeDiscountPreviewRequest request)
        {
            if (request == null)
                return BadRequest(ApiResponse.Fail("Preview request is required."));

            request.BookingId = bookingId;

            if (User.IsLocationBoundRole() && !await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Booking, bookingId.ToString()))
                return StatusCode(403, ApiResponse.Fail("Access denied: booking belongs to another location."));

            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            var result = await _service.PreviewAsync(request, ResolveUserEmail(), allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        [HttpPost("api/one-time-discounts/preview")]
        public async Task<IActionResult> Preview([FromBody] OneTimeDiscountPreviewRequest request)
        {
            if (request == null || request.BookingId <= 0)
                return BadRequest(ApiResponse.Fail("Valid BookingId is required."));

            if (User.IsLocationBoundRole() && !await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Booking, request.BookingId.ToString()))
                return StatusCode(403, ApiResponse.Fail("Access denied: booking belongs to another location."));

            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            var result = await _service.PreviewAsync(request, ResolveUserEmail(), allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        [HttpPost("api/booking/{bookingId:int}/one-time-discounts")]
        [RecordScope(RecordKind.Booking, "bookingId")]
        public async Task<IActionResult> CreateForBooking(int bookingId, [FromBody] OneTimeDiscountCreateRequest request)
        {
            if (request == null)
                return BadRequest(ApiResponse.Fail("Request payload is required."));

            request.BookingId = bookingId;

            if (User.IsLocationBoundRole() && !await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Booking, bookingId.ToString()))
                return StatusCode(403, ApiResponse.Fail("Access denied: booking belongs to another location."));

            var actorId = await ResolveActorIdAsync();
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            var result = await _service.CreateAsync(request, actorId, ResolveUserEmail(), allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        [HttpPost("api/one-time-discounts")]
        public async Task<IActionResult> Create([FromBody] OneTimeDiscountCreateRequest request)
        {
            if (request == null || request.BookingId <= 0)
                return BadRequest(ApiResponse.Fail("Valid BookingId is required."));

            if (User.IsLocationBoundRole() && !await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Booking, request.BookingId.ToString()))
                return StatusCode(403, ApiResponse.Fail("Access denied: booking belongs to another location."));

            var actorId = await ResolveActorIdAsync();
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            var result = await _service.CreateAsync(request, actorId, ResolveUserEmail(), allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        [HttpPost("api/booking/{bookingId:int}/one-time-discounts/{id:int}/cancel")]
        [HttpPost("api/one-time-discounts/{id:int}/cancel")]
        [HttpDelete("api/booking/{bookingId:int}/one-time-discounts/{id:int}")]
        [HttpDelete("api/one-time-discounts/{id:int}")]
        public async Task<IActionResult> Cancel(int id, int? bookingId = null)
        {
            if (bookingId.HasValue && bookingId.Value > 0 && User.IsLocationBoundRole()
                && !await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Booking, bookingId.Value.ToString()))
            {
                return StatusCode(403, ApiResponse.Fail("Access denied: booking belongs to another location."));
            }

            var actorId = await ResolveActorIdAsync();
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            var result = await _service.CancelAsync(id, actorId, ResolveUserEmail(), allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }
    }
}
