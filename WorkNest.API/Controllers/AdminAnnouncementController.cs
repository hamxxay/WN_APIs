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

            var currentUserId = await ResolveCurrentUserIdAsync();
            var result = await _announcementService.CreateAnnouncementAsync(request, currentUserId);

            if (!result.IsSuccessful)
                return BadRequest(result);

            return StatusCode(201, result);
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
