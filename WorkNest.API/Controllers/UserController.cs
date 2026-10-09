using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.User;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Common.Constants;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")] // user records: staff only; writes are admin-only below
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
            // Location-bound roles (admin, sales executive) see users of ALL their locations, not just the main one.
            var (items, total) = await MultiLocationList.FetchAsync(User.ScopedLocations(null), page, limit,
                (p, l, loc) => _users.GetUsersAsync(p, l, search, loc));
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

        // ── User management (write) — admin / super admin only ─────────────────────
        // Only a super admin may create, change or remove a super admin, or grant the super admin role.
        private const string ManageRoles = "admin,Admin,super_admin,SuperAdmin";

        /// <summary>An admin tied to locations may only hand out those locations (super admins: any).</summary>
        private bool AssignsForeignLocation(int? locationId, List<int>? locationIds)
        {
            if (!User.IsLocationBoundRole()) return false;
            var own = User.GetLocationIds();
            var requested = (locationIds ?? new List<int>()).Concat(locationId is > 0 ? new[] { locationId.Value } : Array.Empty<int>());
            return requested.Any(x => x > 0 && !own.Contains(x));
        }

        [Authorize(Roles = ManageRoles)]
        [HttpPost("api/user")]
        public async Task<IActionResult> Create([FromBody] UserCreateRequest request)
        {
            if (GrantsSuperAdmin(request?.Role)) return Forbid();
            if (AssignsForeignLocation(request?.LocationId, request?.LocationIds))
                return BadRequest(ApiResponse.Fail("You can only assign locations you are assigned to yourself."));
            var result = await _users.CreateUserAsync(request!, null);
            return StatusCode(201, result);
        }

        [Authorize(Roles = ManageRoles)]
        [HttpPut("api/user/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UserUpdateRequest request)
        {
            if (GrantsSuperAdmin(request?.Role) || await IsProtectedSuperAdminAsync(id)) return Forbid();
            if (AssignsForeignLocation(request?.LocationId, request?.LocationIds))
                return BadRequest(ApiResponse.Fail("You can only assign locations you are assigned to yourself."));
            return Ok(await _users.UpdateUserAsync(id, request!));
        }

        [Authorize(Roles = ManageRoles)]
        [HttpPut("api/user/{publicId:guid}")]
        public async Task<IActionResult> UpdateByGuid(Guid publicId, [FromBody] UserUpdateRequest request)
        {
            var id = await ResolveIdAsync(publicId);
            if (id is null) return NotFound(ApiResponse.Fail("User not found"));
            return await Update(id.Value, request);
        }

        [Authorize(Roles = ManageRoles)]
        [HttpDelete("api/user/{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            if (await IsProtectedSuperAdminAsync(id)) return Forbid();
            return Ok(await _users.DeleteUserAsync(id));
        }

        [Authorize(Roles = ManageRoles)]
        [HttpDelete("api/user/{publicId:guid}")]
        public async Task<IActionResult> DeleteByGuid(Guid publicId)
        {
            var id = await ResolveIdAsync(publicId);
            if (id is null) return NotFound(ApiResponse.Fail("User not found"));
            return await Delete(id.Value);
        }

        [Authorize(Roles = ManageRoles)]
        [HttpPatch("api/user/{id:int}/activate")]
        public async Task<IActionResult> Activate(int id)
        {
            if (await IsProtectedSuperAdminAsync(id)) return Forbid();
            return Ok(await _users.ActivateUserAsync(id));
        }

        [Authorize(Roles = ManageRoles)]
        [HttpPatch("api/user/{publicId:guid}/activate")]
        public async Task<IActionResult> ActivateByGuid(Guid publicId)
        {
            var id = await ResolveIdAsync(publicId);
            if (id is null) return NotFound(ApiResponse.Fail("User not found"));
            return await Activate(id.Value);
        }

        [Authorize(Roles = ManageRoles)]
        [HttpPatch("api/user/{id:int}/deactivate")]
        public async Task<IActionResult> Deactivate(int id)
        {
            if (await IsProtectedSuperAdminAsync(id)) return Forbid();
            return Ok(await _users.DeactivateUserAsync(id));
        }

        [Authorize(Roles = ManageRoles)]
        [HttpPatch("api/user/{publicId:guid}/deactivate")]
        public async Task<IActionResult> DeactivateByGuid(Guid publicId)
        {
            var id = await ResolveIdAsync(publicId);
            if (id is null) return NotFound(ApiResponse.Fail("User not found"));
            return await Deactivate(id.Value);
        }

        [Authorize(Roles = ManageRoles)]
        [HttpPatch("api/user/{id:int}/role")]
        public async Task<IActionResult> UpdateRole(int id, [FromBody] UserRoleUpdateRequest request)
        {
            if (GrantsSuperAdmin(request?.Role) || await IsProtectedSuperAdminAsync(id)) return Forbid();
            return Ok(await _users.UpdateUserRoleAsync(id, request!));
        }

        [Authorize(Roles = ManageRoles)]
        [HttpPatch("api/user/{publicId:guid}/role")]
        public async Task<IActionResult> UpdateRoleByGuid(Guid publicId, [FromBody] UserRoleUpdateRequest request)
        {
            var id = await ResolveIdAsync(publicId);
            if (id is null) return NotFound(ApiResponse.Fail("User not found"));
            return await UpdateRole(id.Value, request);
        }

        private bool CallerIsSuperAdmin() =>
            User.IsInRole("super_admin") || User.IsInRole("SuperAdmin") || User.IsSuperAdmin();

        /// <summary>True when a non-super-admin tries to assign the super admin role.</summary>
        private bool GrantsSuperAdmin(string? role) =>
            !string.IsNullOrWhiteSpace(role) && Roles.IsSuperAdmin(Roles.ParseRoleId(role)) && !CallerIsSuperAdmin();

        /// <summary>True when a non-super-admin targets an existing super admin account.</summary>
        private async Task<bool> IsProtectedSuperAdminAsync(int id)
        {
            if (CallerIsSuperAdmin()) return false;
            var row = await _db.GetUserByIdAsync(id);
            return row != null && row.TryGetValue("RoleId", out var roleId) && roleId != null
                   && Roles.IsSuperAdmin(Convert.ToInt32(roleId));
        }

        private async Task<int?> ResolveIdAsync(Guid publicId)
        {
            var row = await _db.GetUserByPublicIdAsync(publicId);
            if (row is null) return null;
            return row.TryGetValue("Id", out var rid) && rid != null ? Convert.ToInt32(rid) : 0;
        }
    }
}
