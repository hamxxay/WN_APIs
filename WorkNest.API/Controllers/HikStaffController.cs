using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Staff Access — janitors, office boys, etc. on the Hikvision machines (not linked to a booking).
    /// Every Entrance machine is always included; room machines are chosen per staff member.
    /// Super admins, admins and sales executives; admins and sales executives work with their locations' machines and
    /// staff only, and machine-admin users are super-admin only.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]
    [ValidateLocationScope]
    public class HikStaffController : ControllerBase
    {
        private readonly IHikStaffService _staff;

        public HikStaffController(IHikStaffService staff)
        {
            _staff = staff;
        }

        /// <summary>
        /// All staff (tagged machine users) with their machines, validity, block state and credential counts.
        /// </summary>
        [HttpGet("api/hik/staff")]
        public async Task<IActionResult> GetStaff()
        {
            // Machine-admin users are only visible to super admins.
            var result = await _staff.GetStaffAsync(User.CanSeeMachineAdmins(), Scope());
            return Ok(result);
        }

        /// <summary>
        /// Active job tags (Janitor, Office Boy, …).
        /// </summary>
        [HttpGet("api/hik/staff/tags")]
        public async Task<IActionResult> GetTags()
        {
            var result = await _staff.GetTagsAsync();
            return Ok(result);
        }

        /// <summary>
        /// Add a job tag (admins and super admins: tags are shared by every location).
        /// </summary>
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
        [HttpPost("api/hik/staff/tags")]
        public async Task<IActionResult> AddTag([FromBody] HikTagCreateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Name) || request.Name.Trim().Length > 48)
                return BadRequest(new { message = "Tag name is required (max 48 characters)." });

            var result = await _staff.AddTagAsync(request.Name);
            return Ok(result);
        }

        /// <summary>
        /// Add a staff member to every Entrance machine + the selected room machines.
        /// </summary>
        [HttpPost("api/hik/staff")]
        public async Task<IActionResult> CreateStaff([FromBody] HikStaffCreateRequest request)
        {
            if (await ForeignMachinesAsync(request.RoomDeviceIds) is { } refused) return refused;

            request.CallerLocationIds = Scope();
            var result = await _staff.CreateStaffAsync(request);
            return result.Ok ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Change a staff member's room machines and access-until date.
        /// </summary>
        [HttpPut("api/hik/staff/{employeeNo}/machines")]
        public async Task<IActionResult> UpdateMachines(string employeeNo, [FromBody] HikStaffMachinesRequest request)
        {
            if (await GuardStaffAsync(employeeNo) is { } refused) return refused;
            if (await ForeignMachinesAsync(request?.RoomDeviceIds) is { } foreign) return foreign;

            if (request != null) request.CallerLocationIds = Scope();
            var result = await _staff.UpdateStaffMachinesAsync(employeeNo, request);
            return result.Ok ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Block (isEnabled=false) or unblock a staff member on all their machines; credentials stay enrolled.
        /// </summary>
        [HttpPatch("api/hik/staff/{employeeNo}/access")]
        public async Task<IActionResult> SetAccess(string employeeNo, [FromBody] HikStaffEnableRequest request)
        {
            if (await GuardStaffAsync(employeeNo) is { } refused) return refused;

            var result = await _staff.SetStaffEnabledAsync(employeeNo, request?.IsEnabled ?? true, Scope());
            return result.Error == null ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Enroll a fingerprint / card / face, captured on one of the staff member's online machines.
        /// </summary>
        [HttpPost("api/hik/staff/{employeeNo}/{type:regex(^(fingerprint|card|face)$)}")]
        public async Task<IActionResult> Enroll(string employeeNo, string type, [FromBody] HikStaffEnrollRequest request)
        {
            if (await GuardStaffAsync(employeeNo) is { } refused) return refused;
            if (request.CaptureDeviceId > 0 && await ForeignMachinesAsync(new[] { request.CaptureDeviceId }) is { } foreign) return foreign;

            var result = await _staff.EnrollStaffAsync(employeeNo, type, request, Scope());
            return result.Ok ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Remove a staff member from every machine and the members table.
        /// </summary>
        [HttpDelete("api/hik/staff/{employeeNo}")]
        public async Task<IActionResult> DeleteStaff(string employeeNo)
        {
            if (await GuardStaffAsync(employeeNo) is { } refused) return refused;

            var result = await _staff.DeleteStaffAsync(employeeNo, Scope());
            return result.Ok ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Who may change a staff member. Super admins: anyone. Admins: not machine-admin users (only super admins see
        /// them), and only staff whose machines (last roster + queued changes) are all at the admin's locations — 404 when
        /// none are, as if the person did not exist; 403 when they also have access at another location. Null = go ahead.
        /// </summary>
        private async Task<IActionResult?> GuardStaffAsync(string employeeNo)
        {
            if (User.CanSeeMachineAdmins()) return null;
            var staff = (await _staff.GetStaffAsync()).FirstOrDefault(s => s.EmployeeNo == employeeNo);
            if (staff == null) return NotFound(new HikStaffResultDto { MachineId = employeeNo, Error = "Staff member not found." });
            if (staff.IsMachineAdmin) return Forbid();

            var mine = Scope();
            if (mine == null) return null;
            var theirs = await _staff.GetStaffLocationIdsAsync(staff);
            if (theirs.All(mine.Contains)) return null;
            return theirs.Any(mine.Contains)
                ? StatusCode(403, new HikStaffResultDto { MachineId = employeeNo, Error = "This staff member also has access at another location. Only a super admin can change them." })
                : NotFound(new HikStaffResultDto { MachineId = employeeNo, Error = "Staff member not found." });
        }

        /// <summary>403 when a location-bound admin picks a machine of another location (or one that doesn't exist).</summary>
        private async Task<IActionResult?> ForeignMachinesAsync(IEnumerable<int>? deviceIds)
        {
            var mine = Scope();
            if (mine == null || deviceIds == null || await _staff.DevicesInLocationsAsync(deviceIds, mine)) return null;
            return StatusCode(403, new HikStaffResultDto { Error = "You can only choose machines of your own location." });
        }

        /// <summary>Admins work with their locations' machines; super admins with all.</summary>
        private IReadOnlyCollection<int>? Scope() => User.IsLocationBoundRole() ? User.GetLocationIds() : null;
    }
}
