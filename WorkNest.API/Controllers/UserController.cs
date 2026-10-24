using Microsoft.Extensions.Logging;
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
    [RecordScope(RecordKind.User, "id", "publicId", "email")] // location-bound staff: only records of their locations
    public class UserController : ControllerBase
    {
        private readonly IUserService _users;
        private readonly IDbRepository _db;
        private readonly ILogger<UserController>? _logger;
        public UserController(IUserService users, IDbRepository db, ILogger<UserController>? logger = null)
        {
            _users = users;
            _db = db;
            _logger = logger;
        }

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
        // Only a super admin may create, change or remove a super admin, an admin, a CEO, a Sales Manager or an Access Officer,
        // or grant those roles.
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
            if (GrantsAdminRole(request?.Role)) return OnlySuperAdmin();
            if (AssignsForeignLocation(request?.LocationId, request?.LocationIds))
                return BadRequest(ApiResponse.Fail("You can only assign locations you are assigned to yourself."));
            // Creating a user whose account was deleted turns that account back on: like editing it, an old super admin,
            // admin, CEO, sales manager or access officer account needs a super admin.
            if (!string.IsNullOrWhiteSpace(request?.Email)
                && await _db.GetUserSignInStatusAsync(request.Email) is { IsActive: false } deleted
                && await IsProtectedAsync(deleted.Id))
                return OnlySuperAdmin();
            // Bringing back a deleted account with a password sets the password of an existing (someone else's) account.
            if (!string.IsNullOrWhiteSpace(request?.Password) && !CallerIsSuperAdmin()
                && !string.IsNullOrWhiteSpace(request?.Email)
                && await _db.GetUserSignInStatusAsync(request.Email) is { IsActive: false })
                return OnlySuperAdminSetsPassword();
            try
            {
                var result = await _users.CreateUserAsync(request!, null);
                return StatusCode(201, result);
            }
            catch (Exception ex)
            {
                // Admins only (ManageRoles): the reason instead of "An unexpected error occurred", so a failure on the live
                // database (a missing column, procedure or permission) can be told and fixed.
                _logger?.LogError(ex, "Creating user {Email} failed", request?.Email);
                var reason = ex is Microsoft.Data.SqlClient.SqlException sql ? $"database error {sql.Number}: {sql.Message}" : ex.Message;
                return StatusCode(500, ApiResponse.Fail($"The user could not be created: {reason}"));
            }
        }

        [Authorize(Roles = ManageRoles)]
        [HttpPut("api/user/{id:int}")]
        [KeepMissingLocation] // no LocationId sent = the user's location stays as it is (not the caller's location)
        public async Task<IActionResult> Update(int id, [FromBody] UserUpdateRequest request)
        {
            // Everyone may edit their own profile, but not their own role or locations (an admin can't promote
            // themselves or add locations); the profile fields are saved and the rest is left as it is.
            if (request != null && !CallerIsSuperAdmin() && await IsSelfAsync(id))
            {
                request.Role = null;
                request.LocationId = null;
                request.LocationIds = null;
                return Ok(await _users.UpdateUserAsync(id, request));
            }
            // Setting another user's password would let the caller sign in as them (account takeover).
            if (!string.IsNullOrWhiteSpace(request?.Password) && !CallerIsSuperAdmin()) return OnlySuperAdminSetsPassword();
            if (GrantsAdminRole(request?.Role) || await IsProtectedAsync(id)) return OnlySuperAdmin();
            if (AssignsForeignLocation(request?.LocationId, request?.LocationIds))
                return BadRequest(ApiResponse.Fail("You can only assign locations you are assigned to yourself."));
            return Ok(await _users.UpdateUserAsync(id, request!));
        }

        [Authorize(Roles = ManageRoles)]
        [HttpPut("api/user/{publicId:guid}")]
        [KeepMissingLocation]
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
            if (await IsSelfAsync(id)) return NotOnYourself("delete");
            if (await IsProtectedAsync(id)) return OnlySuperAdmin();
            if (await HasNoLocationForBoundCallerAsync(id)) return NoLocationOnlySuperAdmin("delete");
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
            if (await IsSelfAsync(id)) return NotOnYourself("activate");
            if (await IsProtectedAsync(id)) return OnlySuperAdmin();
            if (await HasNoLocationForBoundCallerAsync(id)) return NoLocationOnlySuperAdmin("activate");
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
            if (await IsSelfAsync(id)) return NotOnYourself("deactivate");
            if (await IsProtectedAsync(id)) return OnlySuperAdmin();
            if (await HasNoLocationForBoundCallerAsync(id)) return NoLocationOnlySuperAdmin("deactivate");
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
            if (await IsSelfAsync(id)) return NotOnYourself("change the role of");
            if (GrantsAdminRole(request?.Role) || await IsProtectedAsync(id)) return OnlySuperAdmin();
            if (await HasNoLocationForBoundCallerAsync(id)) return NoLocationOnlySuperAdmin("change the role of");
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

        /// <summary>
        /// Only a Super Admin creates, promotes or changes Admins, CEOs, Sales Managers, Access Officers (and Super Admins). An Admin
        /// manages Sales Executives and customers, giving them one, some or all of the Admin's own locations, and edits
        /// their own profile (not their own role, locations or status).
        /// </summary>
        private bool GrantsAdminRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role) || CallerIsSuperAdmin()) return false;
            return IsProtectedRole(Roles.ParseRoleId(role));
        }

        /// <summary>True when a non-super-admin targets an existing Admin, CEO, Sales Manager, Access Officer or Super Admin account.</summary>
        private async Task<bool> IsProtectedAsync(int id)
        {
            if (CallerIsSuperAdmin()) return false;
            var row = await _db.GetUserByIdAsync(id);
            if (row == null || !row.TryGetValue("RoleId", out var roleId) || roleId == null) return false;
            return IsProtectedRole(Convert.ToInt32(roleId));
        }

        /// <summary>Roles only a Super Admin may give, change or take away: Super Admin, Admin, CEO, Sales Manager, Access Officer.</summary>
        private static bool IsProtectedRole(int roleId) =>
            Roles.IsSuperAdmin(roleId) || roleId == Roles.AdminId || Roles.IsCeo(roleId) || Roles.IsSalesManager(roleId)
            || Roles.IsAccessOfficer(roleId);

        public const string OnlySuperAdminPasswordMessage = "Only a super admin can change another user's password.";

        private IActionResult OnlySuperAdminSetsPassword() => StatusCode(403, ApiResponse.Fail(OnlySuperAdminPasswordMessage));

        /// <summary>
        /// True when the caller is location-bound (admin, sales executive, ...) and the user belongs to no location at all
        /// (no own / extra location and, for a customer, no booking). [RecordScope] lets everyone reach such a user (e.g. a
        /// customer with no booking yet), so a location-bound admin may read them but not delete, activate, deactivate or
        /// re-role them.
        /// </summary>
        private async Task<bool> HasNoLocationForBoundCallerAsync(int id)
        {
            if (!User.IsLocationBoundRole()) return false;
            var repo = HttpContext?.RequestServices?.GetService(typeof(IRecordScopeRepository)) as IRecordScopeRepository;
            if (repo == null) return false;
            var locations = await repo.GetLocationIdsAsync(RecordKind.User, id.ToString());
            return locations is { Count: 0 };
        }

        private IActionResult NoLocationOnlySuperAdmin(string action) =>
            StatusCode(403, ApiResponse.Fail($"This user has no booking at your locations; only a super admin can {action} them."));

        private IActionResult OnlySuperAdmin() =>
            StatusCode(403, ApiResponse.Fail("Only a Super Admin can create or change Admin, CEO, Sales Manager and Access Officer accounts."));

        /// <summary>True when the user record is the signed-in user's own account (matched on the JWT email).</summary>
        private async Task<bool> IsSelfAsync(int id)
        {
            var email = User.GetEmail();
            if (string.IsNullOrWhiteSpace(email)) return false;
            var row = await _db.GetUserByIdAsync(id);
            var rowEmail = row != null && row.TryGetValue("Email", out var e) ? e?.ToString() : null;
            return !string.IsNullOrWhiteSpace(rowEmail)
                && string.Equals(rowEmail.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Nobody deletes, locks or re-roles their own account (it would lock them out or let them promote themselves).</summary>
        private IActionResult NotOnYourself(string action) =>
            StatusCode(403, ApiResponse.Fail($"You can't {action} your own account."));

        private async Task<int?> ResolveIdAsync(Guid publicId)
        {
            var row = await _db.GetUserByPublicIdAsync(publicId);
            if (row is null) return null;
            return row.TryGetValue("Id", out var rid) && rid != null ? Convert.ToInt32(rid) : 0;
        }
    }
}
