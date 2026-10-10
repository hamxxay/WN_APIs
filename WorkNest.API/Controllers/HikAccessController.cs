using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.Interfaces;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Access Dashboard, Access Activity Log and Access Analytics (admin / super admin only).
    /// Read-only over the HIK tables; event times are the terminals' local time.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")]
    [ValidateLocationScope]
    public class HikAccessController : ControllerBase
    {
        private readonly IHikAccessService _access;
        private readonly IBusinessClock _clock;

        public HikAccessController(IHikAccessService access, IBusinessClock clock)
        {
            _access = access;
            _clock = clock;
        }

        /// <summary>
        /// KPIs, machines, today's hourly inflow, latest entries and members expiring soon.
        /// </summary>
        [HttpGet("api/hik/access/dashboard")]
        public async Task<IActionResult> GetDashboard([FromQuery] int expiringDays = 7)
        {
            var result = await _access.GetDashboardAsync(expiringDays, Scope());
            return Ok(result);
        }

        /// <summary>
        /// Door/terminal entries (who entered). from/to accept a date or date-time; to is exclusive.
        /// </summary>
        [HttpGet("api/hik/access/events")]
        public async Task<IActionResult> GetEvents(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int? deviceId = null,
            [FromQuery] string? employeeNo = null,
            [FromQuery] string? name = null,
            [FromQuery] int limit = 500)
        {
            var result = await _access.GetEventsAsync(from, to, deviceId, employeeNo, name, limit, Scope());
            return Ok(result);
        }

        /// <summary>
        /// Dashboard / sync activity (WN_HIK_SyncLog): machine online/offline, enrollments, syncs.
        /// </summary>
        [HttpGet("api/hik/access/activity")]
        public async Task<IActionResult> GetActivity([FromQuery] int limit = 200)
        {
            var result = await _access.GetSyncActivityAsync(limit, Scope());
            return Ok(result);
        }

        /// <summary>
        /// Traffic analytics for an inclusive date range (defaults to today).
        /// </summary>
        [HttpGet("api/hik/access/analytics")]
        public async Task<IActionResult> GetAnalytics([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            var result = await _access.GetAnalyticsAsync(from ?? _clock.Today, to ?? from ?? _clock.Today, Scope());
            return Ok(result);
        }

        /// <summary>
        /// One member's scans for an inclusive date range, matched by employee # (or name when no #).
        /// </summary>
        [HttpGet("api/hik/access/analytics/user")]
        public async Task<IActionResult> GetUserAnalytics(
            [FromQuery] string? employeeNo = null,
            [FromQuery] string? name = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            if (string.IsNullOrWhiteSpace(employeeNo) && string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "employeeNo or name is required." });

            var result = await _access.GetUserAnalyticsAsync(employeeNo, name, from ?? _clock.Today, to ?? from ?? _clock.Today, Scope());
            return Ok(result);
        }

        /// <summary>Admins / sales executives / receptionists see their locations' machines, scans and members; super admins all.</summary>
        private IReadOnlyCollection<int>? Scope() => User.IsLocationBoundRole() ? User.GetLocationIds() : null;
    }
}
