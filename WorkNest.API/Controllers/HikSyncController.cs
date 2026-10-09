using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Hikvision machine ⇄ database sync (replaces the HIK Node scheduler). Status of every job, and a manual
    /// "run now" for one job. Admins and super admins only.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
    public class HikSyncController : ControllerBase
    {
        private readonly IHikSyncService _sync;
        private readonly HikSyncOptions _options;

        public HikSyncController(IHikSyncService sync, HikSyncOptions options)
        {
            _sync = sync;
            _options = options;
        }

        /// <summary>Whether the schedule is on, the job names, and the last run of each job.</summary>
        [HttpGet("api/hik/sync/status")]
        public IActionResult Status() =>
            Ok(ApiResponse.Ok(new { enabled = _options.Enabled, fullOnlineSweep = _options.FullOnlineSweep, jobs = _sync.GetStatus() }));

        /// <summary>
        /// Run one job now: online, maintenance, watch, clock, events, snapshots, users, cards, facevault,
        /// renewals, expiry, grants, credentials, queue. Returns immediately with the current status if that
        /// job is already running.
        /// </summary>
        /// <summary>"Test" button: connects to the machine now and saves whether it is online.</summary>
        [HttpPost("api/hik/devices/{id:int}/test")]
        public async Task<IActionResult> TestDevice(int id)
        {
            var result = await _sync.TestDeviceAsync(id);
            if (result == null) return NotFound(ApiResponse.Fail("Machine not found."));
            return Ok(ApiResponse.Ok(result, result.Online ? $"{result.Name} is online." : $"{result.Name} is offline: {result.Error}"));
        }

        /// <summary>"Test all machines": tests every machine in parallel and saves online / offline.</summary>
        [HttpPost("api/hik/devices/test-all")]
        public async Task<IActionResult> TestAllDevices()
        {
            var results = await _sync.TestAllDevicesAsync();
            var online = results.Count(r => r.Online);
            return Ok(ApiResponse.Ok(results, $"{online} of {results.Count} machines online."));
        }

        [HttpPost("api/hik/sync/run/{job}")]
        public async Task<IActionResult> Run(string job)
        {
            if (!_sync.JobNames.Contains((job ?? string.Empty).Trim().ToLowerInvariant()))
                return BadRequest(ApiResponse.Fail($"Unknown job. Jobs: {string.Join(", ", _sync.JobNames)}."));
            var status = await _sync.RunJobAsync(job!);
            return Ok(ApiResponse.Ok(status, status.LastOk == false ? "Job finished with an error." : "Job finished."));
        }
    }
}
