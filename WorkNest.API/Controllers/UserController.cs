using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.User;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly IUserService _users;
        public UserController(IUserService users) => _users = users;

        [HttpGet("api/user")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string search = "")
        {
            var all = await _users.GetAllUsersAsync();
            var (items, total) = PaginationHelper.Paginate(all, page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpGet("api/user/{id}")]
        public async Task<IActionResult> Get(string id)
        {
            var result = await _users.GetUserByIdAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("api/user/{id}/history")]
        public async Task<IActionResult> History(string id)
        {
            var result = await _users.GetUserHistoryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/user")]
        public async Task<IActionResult> Create([FromBody] UserCreateRequest request)
        {
            var result = await _users.CreateUserAsync(request);
            return StatusCode(201, result);
        }

        [HttpPut("api/user/{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UserUpdateRequest request) =>
            Ok(await _users.UpdateUserAsync(id, request));

        [HttpDelete("api/user/{id}")]
        public async Task<IActionResult> Delete(string id) =>
            Ok(await _users.DeleteUserAsync(id));

        [HttpPatch("api/user/{id}/activate")]
        public async Task<IActionResult> Activate(string id) =>
            Ok(await _users.ActivateUserAsync(id));

        [HttpPatch("api/user/{id}/deactivate")]
        public async Task<IActionResult> Deactivate(string id) =>
            Ok(await _users.DeactivateUserAsync(id));

        [HttpPatch("api/user/{id}/role")]
        public async Task<IActionResult> UpdateRole(string id, [FromBody] UserRoleUpdateRequest request) =>
            Ok(await _users.UpdateUserRoleAsync(id, request));
    }
}
