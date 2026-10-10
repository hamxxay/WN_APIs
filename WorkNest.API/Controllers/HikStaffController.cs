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
    /// </summary>
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")]
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
            // Machine-admin users are only visible to admin / super admin.
            var result = await _staff.GetStaffAsync(User.IsAdminOrSuperAdmin(), Scope());
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
        /// Add a job tag (admin / super admin only).
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
            if (await IsHiddenMachineAdminAsync(employeeNo)) return Forbid();

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
            if (await IsHiddenMachineAdminAsync(employeeNo)) return Forbid();

            var result = await _staff.SetStaffEnabledAsync(employeeNo, request?.IsEnabled ?? true);
            return result.Error == null ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Enroll a fingerprint / card / face, captured on one of the staff member's online machines.
        /// </summary>
        [HttpPost("api/hik/staff/{employeeNo}/{type:regex(^(fingerprint|card|face)$)}")]
        public async Task<IActionResult> Enroll(string employeeNo, string type, [FromBody] HikStaffEnrollRequest request)
        {
            if (await IsHiddenMachineAdminAsync(employeeNo)) return Forbid();

            var result = await _staff.EnrollStaffAsync(employeeNo, type, request);
            return result.Ok ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Remove a staff member from every machine and the members table.
        /// </summary>
        [HttpDelete("api/hik/staff/{employeeNo}")]
        public async Task<IActionResult> DeleteStaff(string employeeNo)
        {
            if (await IsHiddenMachineAdminAsync(employeeNo)) return Forbid();

            var result = await _staff.DeleteStaffAsync(employeeNo);
            return result.Ok ? Ok(result) : BadRequest(result);
        }
    
        /// <summary>Non-admins may not change machine-admin users (they can't see them either).</summary>
        private async Task<bool> IsHiddenMachineAdminAsync(string employeeNo)
        {
            if (User.IsAdminOrSuperAdmin()) return false;
            var staff = await _staff.GetStaffAsync();
            return staff.Any(s => s.EmployeeNo == employeeNo && s.IsMachineAdmin);
        }

        /// <summary>Admins / sales executives / receptionists work with their locations' machines; super admins with all.</summary>
        private IReadOnlyCollection<int>? Scope() => User.IsLocationBoundRole() ? User.GetLocationIds() : null;
    }
}
