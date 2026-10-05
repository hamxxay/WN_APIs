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
            var result = await _deviceService.GetDevicesAsync(location);
            return Ok(result);
        }

        /// <summary>
        /// Retrieves user rosters aggregated across all machines plus CNIC mappings.
        /// </summary>
        [HttpGet("api/roster")]
        [HttpGet("api/hik/roster")]
        public async Task<IActionResult> GetRosters([FromQuery] string? location = null)
        {
            // Machine-admin users are only visible to admin / super admin.
            var result = await _deviceService.GetRostersAsync(location, User.IsAdminOrSuperAdmin());
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
}
