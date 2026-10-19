using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// CEO dashboard: read-only, every location. The CEO (role ceo) sees only these endpoints (and the forecast);
    /// super admins may open them too. Common query parameters: period = month (default) | quarter | year (to date,
    /// against the previous period, as api/dashboard/overview) | custom (CS_01: a day range, against the same number of
    /// days just before it; dashboard and rooms: from / to, breakdown and records: periodFrom / periodTo, yyyy-MM-dd, at
    /// most 366 days, not after today) and locationId (narrow to one location; tour inquiries by
    /// their branch once the database update QA_07 has run, before it they count every location and say so).
    /// Bad parameters answer 400; before the database update CEO_01 has run, 503 (records/leases: before QA_07; rooms:
    /// before CEO_02 a 200 with available = false; a custom period: before CS_01).
    /// </summary>
    [ApiController]
    [Authorize(Roles = "ceo,super_admin,SuperAdmin")]
    [CeoAllowed]
    public class CeoDashboardController : ControllerBase
    {
        private readonly ICeoDashboardService _ceo;
        private readonly ILogger<CeoDashboardController> _logger;

        public CeoDashboardController(ICeoDashboardService ceo, ILogger<CeoDashboardController> logger)
        {
            _ceo = ceo;
            _logger = logger;
        }

        /// <summary>Level 1: money, occupancy, sales funnel and team, plus the locations for the filter.</summary>
        [HttpGet("api/ceo/dashboard")]
        public Task<IActionResult> Dashboard([FromQuery] string? period, [FromQuery] int? locationId,
            [FromQuery] string? from = null, [FromQuery] string? to = null) =>
            Run(() => _ceo.GetDashboardAsync(period, locationId, from, to));

        /// <summary>
        /// Level 2: one chart element broken down. metric = revenue (month) | aging (bucket) | occupancy-type
        /// (spaceTypeId) | occupancy-location (locationId) | bookings-month (month) | inquiries-type (type) | staff (userId).
        /// revenue takes from / to (issued in that range, CS_01) instead of a month for a custom period.
        /// </summary>
        [HttpGet("api/ceo/breakdown")]
        public Task<IActionResult> Breakdown([FromQuery] CeoBreakdownQuery query) =>
            Run(() => _ceo.GetBreakdownAsync(query));

        /// <summary>
        /// Level 3: records behind a breakdown row, newest first (leases: soonest first), at most 200 (total = all that
        /// match). kind = invoices | spaces | bookings | inquiries | quotations | leases (QA_07: confirmed leases ending in
        /// the next 90 days, or month / from / to). invoices: from / to = issued in that range (CS_01).
        /// </summary>
        [HttpGet("api/ceo/records/{kind}")]
        public Task<IActionResult> Records(string kind, [FromQuery] CeoRecordsQuery query) =>
            Run(() => _ceo.GetRecordsAsync(kind, query));

        /// <summary>
        /// Rooms (private offices, shared spaces): occupancy now and in the period, and each confirmed booking's price
        /// per seat against the list price (24/7) or the shift price (Morning / Evening), under / at / over. Before the
        /// database update CEO_02: 200 with available = false and a message.
        /// </summary>
        [HttpGet("api/ceo/rooms")]
        public Task<IActionResult> Rooms([FromQuery] string? period, [FromQuery] int? locationId,
            [FromQuery] string? from = null, [FromQuery] string? to = null) =>
            Run(() => _ceo.GetRoomReportAsync(period, locationId, from, to));

        /// <summary>
        /// Network (UniFi): clients online now and over time (stored history), devices online / offline by type, clients
        /// by type, internet lines and latency, and the critical issues, for the UniFi sites that serve the chosen location
        /// (every site without one). Read-only: built from what the Network pages already poll. UniFi not set up or not
        /// reachable: 200 with available = false and a short message (the rest of the dashboard does not depend on it).
        /// </summary>
        [HttpGet("api/ceo/network")]
        public async Task<IActionResult> Network([FromQuery] int? locationId, [FromServices] INetworkDashboardService network) =>
            Ok(ApiResponse.Ok(await network.GetAsync(new NetworkScope(null, locationId is > 0 ? locationId : null),
                HttpContext?.RequestAborted ?? default)));

        private async Task<IActionResult> Run(Func<Task<ApiResponse>> action)
        {
            try
            {
                var result = await action();
                return result.IsSuccessful ? Ok(result) : BadRequest(result);
            }
            catch (CeoDatabaseUpdateMissingException ex)
            {
                _logger.LogWarning("CEO dashboard called before the database update {Update}: {Error}", ex.Update, ex.InnerException?.Message);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(ex.Message));
            }
        }
    }
}
