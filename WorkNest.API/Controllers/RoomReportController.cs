using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Staff Room Report: who used a room's door machine(s) over a period (entries, denied scans, exit-button presses).
    /// Admin / super admin only; location-bound admins only get their own locations' machines. Read-only.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
    public class RoomReportController : ControllerBase
    {
        private readonly IRoomReportService _report;

        public RoomReportController(IRoomReportService report) => _report = report;

        /// <summary>GET api/admin/room-report?deviceIds=49,50&amp;customerId=&amp;from=2026-10-01&amp;to=2026-10-31 (to inclusive, max 92 days).</summary>
        [HttpGet("api/admin/room-report")]
        public async Task<IActionResult> Get([FromQuery] string? deviceIds = null, [FromQuery] int? customerId = null,
            [FromQuery] string? from = null, [FromQuery] string? to = null)
        {
            if (!TryDate(from, out var f)) return BadRequest(ApiResponse.Fail("'from' must be a date like 2026-10-01."));
            if (!TryDate(to, out var t)) return BadRequest(ApiResponse.Fail("'to' must be a date like 2026-10-31."));
            if (t < f) return BadRequest(ApiResponse.Fail("'to' must not be before 'from'."));
            if ((t - f).TotalDays + 1 > RoomReportService.MaxDays)
                return BadRequest(ApiResponse.Fail($"The period can be at most {RoomReportService.MaxDays} days."));

            var ids = new List<int>();
            foreach (var part in (deviceIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                    return BadRequest(ApiResponse.Fail($"Invalid machine id '{part}'."));
                ids.Add(id);
            }
            if (customerId is <= 0) return BadRequest(ApiResponse.Fail("Invalid customerId."));
            if (ids.Count == 0 && customerId == null)
                return BadRequest(ApiResponse.Fail("Pick at least one machine (deviceIds) or a customer (customerId)."));

            var scope = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            var outcome = await _report.BuildAsync(ids, customerId, f, t, scope);
            if (outcome.Report != null) return Ok(ApiResponse.Ok(outcome.Report));
            return StatusCode(outcome.Status, ApiResponse.Fail(outcome.Message));
        }

        private static bool TryDate(string? s, out DateTime d) =>
            DateTime.TryParseExact((s ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
    }
}
