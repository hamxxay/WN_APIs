using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;

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

        /// <summary>
        /// Retrieves user rosters aggregated across all machines plus CNIC mappings.
        /// </summary>
        [HttpGet("api/roster")]
        [HttpGet("api/hik/roster")]
        public async Task<IActionResult> GetRosters([FromQuery] string? location = null)
        {
            // Machine-admin users are only visible to admin / super admin.
            var result = await _deviceService.GetRostersAsync(location, User.IsAdminOrSuperAdmin(), User.IsLocationBoundRole() ? User.GetLocationIds() : null);
            return Ok(result);
        }

        /// <summary>
        /// Retrieves user roster snapshot for a specific device.
        /// </summary>
        [HttpGet("api/devices/{id:int}/users")]
        [HttpGet("api/hik/devices/{id:int}/users")]
        public async Task<IActionResult> GetDeviceUsers(int id)
        {
            var result = await _deviceService.GetDeviceUsersAsync(id, User.IsAdminOrSuperAdmin());
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
