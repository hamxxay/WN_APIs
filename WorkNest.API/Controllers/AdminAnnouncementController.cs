using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Announcement;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")]
    [Route("api/admin/announcements")]
    public class AdminAnnouncementController : ControllerBase
    {
        private readonly IAnnouncementService _announcementService;
        private readonly IDbRepository _db;

        public AdminAnnouncementController(IAnnouncementService announcementService, IDbRepository db)
        {
            _announcementService = announcementService;
            _db = db;
        }

        private async Task<int> ResolveCurrentUserIdAsync()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? User.FindFirst("UserId")?.Value
                       ?? User.FindFirst("id")?.Value;

            if (!string.IsNullOrWhiteSpace(idClaim) && int.TryParse(idClaim, out var intId))
            {
                return intId;
            }

            var emailClaim = User.FindFirst(ClaimTypes.Email)?.Value
                          ?? User.FindFirst("email")?.Value
                          ?? User.Identity?.Name;

            if (!string.IsNullOrWhiteSpace(emailClaim))
            {
                var userRow = await _db.GetUserByEmailAsync(emailClaim);
                if (userRow != null && userRow.TryGetValue("Id", out var uid) && uid != null)
                {
                    return Convert.ToInt32(uid);
                }
            }

            return 1; // Default fallback to system/admin user
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateAnnouncementRequest request)
        {
            if (request == null)
                return BadRequest(ApiResponse.Fail("Invalid request body."));

            var scopeError = await CheckTargetsInScopeAsync(request);
            if (scopeError != null) return scopeError;

            var currentUserId = await ResolveCurrentUserIdAsync();
            var result = await _announcementService.CreateAnnouncementAsync(request, currentUserId);

            if (!result.IsSuccessful)
                return BadRequest(result);

            return StatusCode(201, result);
        }

        public const string AllNotAllowed = "You can only send announcements to your own locations. Choose a location, a space or recipients.";
        public const string TargetNotAllowed = "You can only send announcements to your own locations.";

        /// <summary>
        /// Location-bound staff (admin, sales executive): no announcement to everyone (TargetScope "All", also what an
        /// unknown scope becomes) = 400, and every LocationId / SpaceId / CustomUserIds target sent must be in their
        /// locations (403). Null = allowed. Everyone else is not checked.
        /// </summary>
        private async Task<IActionResult?> CheckTargetsInScopeAsync(CreateAnnouncementRequest request)
        {
            if (!User.IsLocationBoundRole()) return null;

            var scope = request.TargetScope;
            if (scope is not ("Location" or "Space" or "CustomList"))
                return BadRequest(ApiResponse.Fail(AllNotAllowed));

            var checks = new List<(RecordKind Kind, int Id)>();
            if (request.LocationId is > 0) checks.Add((RecordKind.Location, request.LocationId.Value));
            if (request.SpaceId is > 0) checks.Add((RecordKind.Space, request.SpaceId.Value));
            if (request.CustomUserIds != null)
                checks.AddRange(request.CustomUserIds.Where(id => id > 0).Distinct().Select(id => (RecordKind.User, id)));

            foreach (var (kind, id) in checks)
                if (!await RecordScopeAttribute.AllowsAsync(HttpContext, kind, id.ToString()))
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail(TargetNotAllowed));
            return null;
        }

        [HttpGet]
        public async Task<IActionResult> GetList(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 20,
            [FromQuery] string? search = null)
        {
            var result = await _announcementService.GetAnnouncementsAsync(page, limit, search);
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _announcementService.GetAnnouncementByIdAsync(id);
            if (!result.IsSuccessful)
                return NotFound(result);

            return Ok(result);
        }
    }
}
