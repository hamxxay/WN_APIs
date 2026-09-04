using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.AccessCard;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class AccessCardController : ControllerBase
    {
        private readonly IAccessCardService _accessCards;
        public AccessCardController(IAccessCardService accessCards) => _accessCards = accessCards;

        private int? ResolveUserId()
        {
            var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("id")?.Value ?? User.FindFirst("sub")?.Value;
            return int.TryParse(sub, out var uid) ? uid : null;
        }

        [HttpGet("api/access-card")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 20,
            [FromQuery] string? search = null,
            [FromQuery] int? bookingId = null,
            [FromQuery] int? customerId = null,
            [FromQuery] int? spaceId = null,
            [FromQuery] int? status = null) =>
            Ok(await _accessCards.GetAllAccessCardsAsync(page, limit, search, bookingId, customerId, spaceId, status));

        [HttpGet("api/access-card/{id}")]
        public async Task<IActionResult> GetById(string id) =>
            Ok(await _accessCards.GetAccessCardByIdAsync(id));

        [HttpPost("api/access-card")]
        public async Task<IActionResult> Create([FromBody] AccessCardRequest request) =>
            StatusCode(201, await _accessCards.CreateAccessCardAsync(request, ResolveUserId()));

        [HttpPut("api/access-card/{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] AccessCardRequest request) =>
            Ok(await _accessCards.UpdateAccessCardAsync(id, request, ResolveUserId()));

        [HttpDelete("api/access-card/{id}")]
        public async Task<IActionResult> Delete(string id) =>
            Ok(await _accessCards.DeleteAccessCardAsync(id));

        [HttpPost("api/access-card/generate/{bookingId}")]
        public async Task<IActionResult> GenerateForBooking(int bookingId) =>
            Ok(await _accessCards.GenerateAccessCardsForBookingAsync(bookingId, ResolveUserId()));
    }
}
