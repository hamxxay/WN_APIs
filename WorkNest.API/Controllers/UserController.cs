using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.User;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

using WorkNest.API.Extensions;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    [ValidateLocationScope]
    public class UserController : ControllerBase
    {
        private readonly IUserService _users;
        private readonly IDbRepository _db;
        public UserController(IUserService users, IDbRepository db) { _users = users; _db = db; }

        [HttpGet("api/user")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null)
        {
            int? filterLocationId = User.IsLocationBoundRole() ? User.GetLocationId() : null;
            var (items, total) = await _users.GetUsersAsync(page, limit, search, filterLocationId);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpGet("api/user/{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            var result = await _users.GetUserByIdAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("api/user/{publicId:guid}")]
        public async Task<IActionResult> GetByGuid(Guid publicId)
        {
            var row = await _db.GetUserByPublicIdAsync(publicId);
            if (row is null) return NotFound(ApiResponse.Fail("User not found"));
            var id = row.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return await Get(id);
        }

        [HttpGet("api/user/{email}")]
        public async Task<IActionResult> GetByEmail(string email)
        {
            var row = await _db.GetUserByEmailAsync(email);
            if (row is null) return NotFound(ApiResponse.Fail("User not found"));
            var id = row.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return await Get(id);
        }

        [HttpGet("api/user/{id:int}/history")]
        public async Task<IActionResult> History(int id)
        {
            var result = await _users.GetUserHistoryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("api/user/{publicId:guid}/history")]
        public async Task<IActionResult> HistoryByGuid(Guid publicId)
        {
            var row = await _db.GetUserByPublicIdAsync(publicId);
            if (row is null) return NotFound(ApiResponse.Fail("User not found"));
            var id = row.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return await History(id);
        }

        [HttpGet("api/user/{email}/history")]
        public async Task<IActionResult> HistoryByEmail(string email)
        {
            var row = await _db.GetUserByEmailAsync(email);
            if (row is null) return NotFound(ApiResponse.Fail("User not found"));
            var id = row.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return await History(id);
        }

        [HttpPost("api/user")]
        public async Task<IActionResult> Create([FromBody] UserCreateRequest request)
        {
            var result = await _users.CreateUserAsync(request, null);
            return StatusCode(201, result);
        }

        [HttpPut("api/user/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UserUpdateRequest request) =>
            Ok(await _users.UpdateUserAsync(id, request));

        [HttpPut("api/user/{publicId:guid}")]
        public async Task<IActionResult> UpdateByGuid(Guid publicId, [FromBody] UserUpdateRequest request)
        {
            var row = await _db.GetUserByPublicIdAsync(publicId);
            if (row is null) return NotFound(ApiResponse.Fail("User not found"));
            var id = row.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _users.UpdateUserAsync(id, request));
        }

        [HttpDelete("api/user/{id:int}")]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _users.DeleteUserAsync(id));

        [HttpDelete("api/user/{publicId:guid}")]
        public async Task<IActionResult> DeleteByGuid(Guid publicId)
        {
            var row = await _db.GetUserByPublicIdAsync(publicId);
            if (row is null) return NotFound(ApiResponse.Fail("User not found"));
            var id = row.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _users.DeleteUserAsync(id));
        }

        [HttpPatch("api/user/{id:int}/activate")]
        public async Task<IActionResult> Activate(int id) =>
            Ok(await _users.ActivateUserAsync(id));

        [HttpPatch("api/user/{publicId:guid}/activate")]
        public async Task<IActionResult> ActivateByGuid(Guid publicId)
        {
            var row = await _db.GetUserByPublicIdAsync(publicId);
            if (row is null) return NotFound(ApiResponse.Fail("User not found"));
            var id = row.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _users.ActivateUserAsync(id));
        }

        [HttpPatch("api/user/{id:int}/deactivate")]
        public async Task<IActionResult> Deactivate(int id) =>
            Ok(await _users.DeactivateUserAsync(id));

        [HttpPatch("api/user/{publicId:guid}/deactivate")]
        public async Task<IActionResult> DeactivateByGuid(Guid publicId)
        {
            var row = await _db.GetUserByPublicIdAsync(publicId);
            if (row is null) return NotFound(ApiResponse.Fail("User not found"));
            var id = row.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _users.DeactivateUserAsync(id));
        }

        [HttpPatch("api/user/{id:int}/role")]
        public async Task<IActionResult> UpdateRole(int id, [FromBody] UserRoleUpdateRequest request) =>
            Ok(await _users.UpdateUserRoleAsync(id, request));

        [HttpPatch("api/user/{publicId:guid}/role")]
        public async Task<IActionResult> UpdateRoleByGuid(Guid publicId, [FromBody] UserRoleUpdateRequest request)
        {
            var row = await _db.GetUserByPublicIdAsync(publicId);
            if (row is null) return NotFound(ApiResponse.Fail("User not found"));
            var id = row.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _users.UpdateUserRoleAsync(id, request));
        }
    }
}
