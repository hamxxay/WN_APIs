using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// "Access Machines" tab of the CMS Network section: the Hikvision access-control machines with their connection
    /// state, and the "Test" / "Test all" buttons. Super admins see every machine; a network engineer only the machines
    /// of the locations they are assigned to (machines without a location are not theirs). Admins and sales executives
    /// are refused. Never returns a username or password. Answers { isSuccessful, message, data }.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "super_admin,SuperAdmin,network_engineer,NetworkEngineer")]
    [NetworkEngineerAllowed]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class NetworkAccessMachinesController : ControllerBase
    {
        public const string OtherLocationMessage = "This machine belongs to a location you aren't assigned to.";

        private readonly IHikDeviceService _devices;
        private readonly IHikSyncService _sync;

        public NetworkAccessMachinesController(IHikDeviceService devices, IHikSyncService sync)
        {
            _devices = devices;
            _sync = sync;
        }

        /// <summary>The machines the caller may see, with host, failover host, port, model and serial.</summary>
        [HttpGet("api/network/access-machines")]
        public Task<IActionResult> List() => Run(async () =>
        {
            var details = await Scoped();
            return Ok(ApiResponse.Ok(details.Select(Map).ToList()));
        });

        /// <summary>"Test": connects to one machine now and saves online / offline, model and serial. 403 for another location's machine.</summary>
        [HttpPost("api/network/access-machines/{id:int}/test")]
        public Task<IActionResult> Test(int id) => Run(async () =>
        {
            var machine = await _devices.GetDeviceAsync(id);
            if (machine == null) return NotFound(ApiResponse.Fail("Machine not found."));
            if (!Allowed(machine)) return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail(OtherLocationMessage));
            var result = await _sync.TestDeviceAsync(id);
            if (result == null) return NotFound(ApiResponse.Fail("Machine not found."));
            var fresh = await _devices.GetDeviceAsync(id) ?? machine;
            return Ok(ApiResponse.Ok(new { machine = Map(fresh), error = result.Error },
                result.Online ? $"{result.Name} is online." : $"{result.Name} is offline: {result.Error}"));
        });

        /// <summary>"Test all": tests every machine the caller may see (a network engineer: their locations only).</summary>
        [HttpPost("api/network/access-machines/test-all")]
        public Task<IActionResult> TestAll() => Run(async () =>
        {
            var ids = (await Scoped()).Select(d => d.Id).ToList();
            var results = ids.Count == 0 ? new List<HikDeviceTestResult>() : await _sync.TestAllDevicesAsync(ids);
            var online = results.Count(r => r.Online);
            return Ok(ApiResponse.Ok(results.Select(r => new { id = r.DeviceId, name = r.Name, online = r.Online, error = r.Error }).ToList(),
                $"{online} of {results.Count} machines online."));
        });

        /// <summary>Super admin: every machine. Network engineer: machines of their locations only (no unassigned ones).</summary>
        private bool Allowed(HikDeviceDetailDto d) =>
            !User.IsNetworkEngineer() || (d.LocationId is { } loc && User.GetLocationIds().Contains(loc));

        private async Task<List<HikDeviceDetailDto>> Scoped()
        {
            var engineer = User.IsNetworkEngineer();
            var mine = engineer ? User.GetLocationIds() : null;
            var list = (await _devices.GetDevicesAsync(null, mine)).Where(d => !engineer || (d.LocationId is { } l && mine!.Contains(l))).ToList();
            var details = await Task.WhenAll(list.Select(d => _devices.GetDeviceAsync(d.Id)));
            return details.Where(d => d != null).Select(d => d!).ToList();
        }

        private static object Map(HikDeviceDetailDto d) => new
        {
            id = d.Id,
            name = d.Name,
            code = d.Code,
            groupName = d.Grp,
            locationId = d.LocationId,
            locationName = d.LocationName,
            host = d.Host,
            host2 = d.Host2,
            port = d.Port,
            model = d.Model,
            serial = d.Serial,
            online = d.Online == 1,
            lastSeen = d.LastSeen
        };

        private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try { return await action(); }
            catch (HikDeviceManagementNotInstalledException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(HikDeviceManagementNotInstalledException.UserMessage));
            }
        }
    }
}
