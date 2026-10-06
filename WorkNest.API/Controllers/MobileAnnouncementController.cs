using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Announcement;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class MobileAnnouncementController : ControllerBase
    {
        private readonly IAnnouncementService _announcementService;
        private readonly IDbRepository _db;

        public MobileAnnouncementController(IAnnouncementService announcementService, IDbRepository db)
        {
            _announcementService = announcementService;
            _db = db;
        }

        /// <summary>Identity from the JWT only (the x-user-email header is no longer trusted).</summary>
        private async Task<int?> ResolveCurrentUserIdAsync()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? User.FindFirst("UserId")?.Value
                       ?? User.FindFirst("id")?.Value;

            if (!string.IsNullOrWhiteSpace(idClaim) && int.TryParse(idClaim, out var intId))
            {
                return intId;
            }

            var email = User.FindFirst(ClaimTypes.Email)?.Value
                     ?? User.FindFirst("email")?.Value
                     ?? User.Identity?.Name;

            if (!string.IsNullOrWhiteSpace(email))
            {
                var userRow = await _db.GetUserByEmailAsync(email);
                if (userRow != null && userRow.TryGetValue("Id", out var uid) && uid != null)
                {
                    return Convert.ToInt32(uid);
                }
            }

            return null;
        }

        [HttpGet("api/mobile/announcements")]
        public async Task<IActionResult> GetAnnouncements(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 20)
        {
            var userId = await ResolveCurrentUserIdAsync();
            if (!userId.HasValue)
                return Unauthorized(ApiResponse.Fail("User identity could not be verified."));

            var result = await _announcementService.GetUserAnnouncementsAsync(userId.Value, page, limit);
            return Ok(result);
        }

        [HttpPost("api/mobile/announcements/{id:guid}/read")]
        public async Task<IActionResult> MarkAsRead(
            Guid id)
        {
            var userId = await ResolveCurrentUserIdAsync();
            if (!userId.HasValue)
                return Unauthorized(ApiResponse.Fail("User identity could not be verified."));

            var result = await _announcementService.MarkAnnouncementReadAsync(id, userId.Value);
            if (!result.IsSuccessful)
                return NotFound(result);

            return Ok(result);
        }

        [HttpPost("api/mobile/device-token")]
        public async Task<IActionResult> RegisterDeviceToken(
            [FromBody] DeviceTokenRequest request)
        {
            var userId = await ResolveCurrentUserIdAsync();
            if (!userId.HasValue)
                return Unauthorized(ApiResponse.Fail("User identity could not be verified."));

            var result = await _announcementService.RegisterDeviceTokenAsync(userId.Value, request);
            return Ok(result);
        }
    }
}
