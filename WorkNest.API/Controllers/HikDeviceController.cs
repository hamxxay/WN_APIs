using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")]
    [ValidateLocationScope]
    public class HikDeviceController : ControllerBase
    {
        private readonly IHikDeviceService _deviceService;

        public HikDeviceController(IHikDeviceService deviceService)
        {
            _deviceService = deviceService;
        }

        /// <summary>
        /// Retrieves list of Hikvision machines/devices (credentials omitted).
        /// </summary>
        [HttpGet("api/devices")]
        [HttpGet("api/hik/devices")]
        public async Task<IActionResult> GetDevices([FromQuery] string? location = null)
        {
            // Admins / sales executives see the machines of their locations (and unassigned ones); super admins see all.
            var result = await _deviceService.GetDevicesAsync(location, User.IsLocationBoundRole() ? User.GetLocationIds() : null);
            return Ok(result);
        }

        /// <summary>Super admin: which WorkNest location a machine belongs to. Body { locationId } (null = unassigned).</summary>
        [HttpPut("api/hik/devices/{id:int}/location")]
        [Authorize(Roles = "super_admin,SuperAdmin")]
        public async Task<IActionResult> SetLocation(int id, [FromBody] HikDeviceLocationRequest? request)
        {
            var ok = await _deviceService.SetDeviceLocationAsync(id, request?.LocationId is > 0 ? request.LocationId : null);
            return ok
                ? Ok(new { isSuccessful = true, message = "Machine location saved." })
                : BadRequest(new { isSuccessful = false, message = "The location could not be saved. Run WN_HIK_Devices_LocationId.txt first, or refresh the machine list." });
        }

        // ---- machine management: admin / super admin -------------------------------------------
        // Admins manage the machines of their locations (and unassigned ones); super admins every machine.
        // Passwords are never returned. 503 before the database script HIK_DEV_01 has run.

        /// <summary>One machine for the edit form (connection details, no password).</summary>
        [HttpGet("api/hik/devices/{id:int}")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
        public Task<IActionResult> GetDevice(int id) => Manage(id, async () =>
        {
            var device = await _deviceService.GetDeviceAsync(id);
            return device == null ? NotFound(ApiResponse.Fail("Machine not found.")) : Ok(ApiResponse.Ok(device));
        });

        /// <summary>Machine groups for the Group select (a group starting with "Entrance" = entrance doors).</summary>
        [HttpGet("api/hik/device-groups")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
        public Task<IActionResult> GetDeviceGroups() =>
            Manage(null, async () => Ok(ApiResponse.Ok(await _deviceService.GetDeviceGroupsAsync())));

        /// <summary>Adds a machine (password and location required; 409 when host + port is already used), then tests it.</summary>
        [HttpPost("api/hik/devices")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
        public Task<IActionResult> AddDevice([FromBody] HikDeviceSaveRequest? request) => Manage(null, async () =>
            Outcome(await _deviceService.AddDeviceAsync(request, User.GetEmail())));

        /// <summary>Changes a machine: fields left out are kept, an empty password keeps the saved one. Then tests it.</summary>
        [HttpPut("api/hik/devices/{id:int}")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
        public Task<IActionResult> UpdateDevice(int id, [FromBody] HikDeviceSaveRequest? request) => Manage(id, async () =>
            Outcome(await _deviceService.UpdateDeviceAsync(id, request, User.GetEmail())));

        /// <summary>Deletes a machine. 409 while its cached roster still has people enrolled, unless ?force=true.</summary>
        [HttpDelete("api/hik/devices/{id:int}")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
        public Task<IActionResult> DeleteDevice(int id, [FromQuery] bool force = false) => Manage(id, async () =>
            Outcome(await _deviceService.DeleteDeviceAsync(id, force, User.GetEmail())));

        /// <summary>Location scope of a machine for admins, and the 503 before HIK_DEV_01.</summary>
        private async Task<IActionResult> Manage(int? deviceId, Func<Task<IActionResult>> action)
        {
            try
            {
                if (deviceId != null && User.IsLocationBoundRole()
                    && !(await _deviceService.GetDevicesAsync(null, User.GetLocationIds())).Any(d => d.Id == deviceId))
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("This machine belongs to a location you aren't assigned to."));
                return await action();
            }
            catch (HikDeviceManagementNotInstalledException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(HikDeviceManagementNotInstalledException.UserMessage));
            }
        }

        private IActionResult Outcome(HikDeviceSaveOutcome o) =>
            StatusCode(o.StatusCode, new ApiResponse { IsSuccessful = o.IsSuccessful, Message = o.Message, Data = o.Device == null ? null : new { device = o.Device, test = o.Test } });

        /// <summary>
        /// Retrieves user rosters aggregated across all machines plus CNIC mappings.
        /// </summary>
        [HttpGet("api/roster")]
        [HttpGet("api/hik/roster")]
        public async Task<IActionResult> GetRosters([FromQuery] string? location = null)
        {
            // Machine-admin users are only visible to admin / super admin.
            var result = await _deviceService.GetRostersAsync(location, User.CanSeeMachineAdmins(), User.IsLocationBoundRole() ? User.GetLocationIds() : null);
            return Ok(result);
        }

        /// <summary>
        /// Retrieves user roster snapshot for a specific device.
        /// </summary>
        [HttpGet("api/devices/{id:int}/users")]
        [HttpGet("api/hik/devices/{id:int}/users")]
        public async Task<IActionResult> GetDeviceUsers(int id)
        {
            var result = await _deviceService.GetDeviceUsersAsync(id, User.CanSeeMachineAdmins());
            return Ok(result);
        }

        /// <summary>
        /// Generates/suggests next employee number (below 8500).
        /// </summary>
        [HttpGet("api/next-employee-no")]
        [HttpGet("api/devices/next-employee-no")]
        [HttpGet("api/hik/next-employee-no")]
        public async Task<IActionResult> GetNextEmployeeNo()
        {
            var result = await _deviceService.GetNextEmployeeNoAsync();
            return Ok(result);
        }
    }

    /// <summary>Body of PUT api/hik/devices/{id}/location.</summary>
    public class HikDeviceLocationRequest
    {
        public int? LocationId { get; set; }
    }
}
