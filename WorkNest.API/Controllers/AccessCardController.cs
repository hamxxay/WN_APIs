using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.AccessCard;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

using WorkNest.API.Extensions;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")]
    [ValidateLocationScope]
    public class AccessCardController : ControllerBase
    {
        private readonly IAccessCardService _accessCards;
        public AccessCardController(IAccessCardService accessCards) => _accessCards = accessCards;

        private int? ResolveUserId()
        {
            var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("id")?.Value ?? User.FindFirst("sub")?.Value;
            return int.TryParse(sub, out var uid) ? uid : null;
        }

        /// <summary>
        /// Location-bound staff (admin, sales executive, ...) see only cards of their locations, and a booking / customer /
        /// space they filter by must be one of their locations (404 otherwise). Super admins / receptionists see all.
        /// </summary>
        [HttpGet("api/access-card")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 20,
            [FromQuery] string? search = null,
            [FromQuery] int? bookingId = null,
            [FromQuery] int? customerId = null,
            [FromQuery] int? spaceId = null,
            [FromQuery] int? status = null)
        {
            IReadOnlyCollection<int>? locationIds = null;
            if (User.IsLocationBoundRole())
            {
                if (bookingId is > 0 && !await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Booking, bookingId.Value.ToString()))
                    return NotFound(ApiResponse.Fail("Booking not found."));
                if (customerId is > 0 && !await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Customer, customerId.Value.ToString()))
                    return NotFound(ApiResponse.Fail("Customer not found."));
                if (spaceId is > 0 && !await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Space, spaceId.Value.ToString()))
                    return NotFound(ApiResponse.Fail("Space not found."));
                locationIds = User.GetLocationIds();
            }
            return Ok(await _accessCards.GetAllAccessCardsAsync(page, limit, search, bookingId, customerId, spaceId, status, locationIds));
        }

        [HttpGet("api/access-card/{id}")]
        [RecordScope(RecordKind.AccessCard, "id")]
        public async Task<IActionResult> GetById(string id) =>
            Ok(await _accessCards.GetAccessCardByIdAsync(id));

        [HttpPost("api/access-card")]
        public async Task<IActionResult> Create([FromBody] AccessCardRequest request)
        {
            var denied = await CheckPartiesAsync(request, null);
            if (denied != null) return denied;
            return StatusCode(201, await _accessCards.CreateAccessCardAsync(request, ResolveUserId()));
        }

        // A missing LocationId keeps the card where it is ([KeepMissingLocation]); a sent one must be the caller's.
        [HttpPut("api/access-card/{id}")]
        [RecordScope(RecordKind.AccessCard, "id")]
        [KeepMissingLocation]
        public async Task<IActionResult> Update(string id, [FromBody] AccessCardRequest request)
        {
            var denied = await CheckPartiesAsync(request, id);
            if (denied != null) return denied;
            return Ok(await _accessCards.UpdateAccessCardAsync(id, request, ResolveUserId()));
        }

        [HttpDelete("api/access-card/{id}")]
        [RecordScope(RecordKind.AccessCard, "id")]
        public async Task<IActionResult> Delete(string id) =>
            Ok(await _accessCards.DeleteAccessCardAsync(id));

        [HttpPost("api/access-card/generate/{bookingId}")]
        [RecordScope(RecordKind.Booking, "bookingId")]
        public async Task<IActionResult> GenerateForBooking(int bookingId) =>
            Ok(await _accessCards.GenerateAccessCardsForBookingAsync(bookingId, ResolveUserId()));

        /// <summary>
        /// The booking in the body must be one of the caller's locations (404), and the card's customer and space must
        /// belong to the booking the card ends up on (400).
        /// </summary>
        private async Task<IActionResult?> CheckPartiesAsync(AccessCardRequest? request, string? existingId)
        {
            if (request == null) return BadRequest(ApiResponse.Fail("Invalid request payload."));
            if (request.BookingId > 0 && !await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Booking, request.BookingId.ToString()))
                return NotFound(ApiResponse.Fail("Booking not found."));
            var (error, _) = await _accessCards.CheckCardPartiesAsync(request, existingId);
            return error == null ? null : BadRequest(ApiResponse.Fail(error));
        }
    }
}
