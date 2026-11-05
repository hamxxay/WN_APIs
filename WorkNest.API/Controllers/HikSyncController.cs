using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.Interfaces;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.API.Extensions;
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
        public async Task<IActionResult> TestDevice(int id, [FromServices] IHikDeviceService devices)
        {
            if (User.IsLocationBoundRole() && !(await devices.GetDevicesAsync(null, User.GetLocationIds())).Any(d => d.Id == id))
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("This machine belongs to a location you aren't assigned to."));
            var result = await _sync.TestDeviceAsync(id);
            if (result == null) return NotFound(ApiResponse.Fail("Machine not found."));
            return Ok(ApiResponse.Ok(result, result.Online ? $"{result.Name} is online." : $"{result.Name} is offline: {result.Error}"));
        }

        /// <summary>"Test all machines": tests every machine in parallel and saves online / offline.</summary>
        [HttpPost("api/hik/devices/test-all")]
        public async Task<IActionResult> TestAllDevices([FromServices] IHikDeviceService devices)
        {
            // Admins test the machines of their locations (+ unassigned); super admins every machine.
            IReadOnlyCollection<int>? only = null;
            if (User.IsLocationBoundRole())
                only = (await devices.GetDevicesAsync(null, User.GetLocationIds())).Select(d => d.Id).ToList();
            var results = await _sync.TestAllDevicesAsync(only);
            var online = results.Count(r => r.Online);
            return Ok(ApiResponse.Ok(results, $"{online} of {results.Count} machines online."));
        }

        [HttpPost("api/hik/sync/run/{job}")]
        [Authorize(Roles = "super_admin,SuperAdmin")] // jobs run across every location
        public async Task<IActionResult> Run(string job)
        {
            if (!_sync.JobNames.Contains((job ?? string.Empty).Trim().ToLowerInvariant()))
                return BadRequest(ApiResponse.Fail($"Unknown job. Jobs: {string.Join(", ", _sync.JobNames)}."));
            var status = await _sync.RunJobAsync(job!);
            return Ok(ApiResponse.Ok(status, status.LastOk == false ? "Job finished with an error." : "Job finished."));
        }

        /// <summary>
        /// "Load missing door events" (CMS Services, super admin): reads every access event of [from, to] (days,
        /// inclusive) from the machines (all, or deviceId) in the background and saves the missing ones. 202 + status;
        /// 409 while one is running; 400 for bad dates.
        /// </summary>
        [HttpPost("api/hik/sync/backfill-events")]
        [Authorize(Roles = "super_admin,SuperAdmin")]
        public async Task<IActionResult> StartBackfill([FromBody] HikEventBackfillRequest? request)
        {
            if (request == null || !DateTime.TryParse(request.From, out var from) || !DateTime.TryParse(request.To, out var to))
                return BadRequest(ApiResponse.Fail("Choose the start and end dates."));
            var (status, error) = await _sync.StartEventBackfillAsync(from, to, request.DeviceId is > 0 ? request.DeviceId : null);
            if (status == null)
                return error != null && error.StartsWith("Missing events are already") ? Conflict(ApiResponse.Fail(error)) : BadRequest(ApiResponse.Fail(error ?? "Could not start."));
            return StatusCode(StatusCodes.Status202Accepted, ApiResponse.Ok(status, "Loading missing door events…"));
        }

        /// <summary>The current / last backfill; { keepFrom } only when none has run since the API started.</summary>
        [HttpGet("api/hik/sync/backfill-events")]
        [Authorize(Roles = "super_admin,SuperAdmin")]
        public IActionResult GetBackfill() =>
            Ok(ApiResponse.Ok(_sync.GetEventBackfill() ?? (object)new { keepFrom = $"{_sync.EventsKeepFrom():yyyy-MM-dd}" }));

        [HttpPost("api/hik/sync/backfill-events/cancel")]
        [Authorize(Roles = "super_admin,SuperAdmin")]
        public IActionResult CancelBackfill() =>
            Ok(ApiResponse.Ok(_sync.CancelEventBackfill() ?? (object)new { keepFrom = $"{_sync.EventsKeepFrom():yyyy-MM-dd}" }, "Cancelling…"));
    }

    public class HikEventBackfillRequest
    {
        public string? From { get; set; }
        public string? To { get; set; }
        public int? DeviceId { get; set; }
    }
}
