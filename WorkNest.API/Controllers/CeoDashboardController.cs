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
    /// against the previous period, as api/dashboard/overview) and locationId (narrow to one location; tour inquiries by
    /// their branch once the database update QA_07 has run, before it they count every location and say so).
    /// Bad parameters answer 400; before the database update CEO_01 has run, 503 (records/leases: before QA_07).
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
        public Task<IActionResult> Dashboard([FromQuery] string? period, [FromQuery] int? locationId) =>
            Run(() => _ceo.GetDashboardAsync(period, locationId));

        /// <summary>
        /// Level 2: one chart element broken down. metric = revenue (month) | aging (bucket) | occupancy-type
        /// (spaceTypeId) | occupancy-location (locationId) | bookings-month (month) | inquiries-type (type) | staff (userId).
        /// </summary>
        [HttpGet("api/ceo/breakdown")]
        public Task<IActionResult> Breakdown([FromQuery] CeoBreakdownQuery query) =>
            Run(() => _ceo.GetBreakdownAsync(query));

        /// <summary>
        /// Level 3: records behind a breakdown row, newest first (leases: soonest first), at most 200 (total = all that
        /// match). kind = invoices | spaces | bookings | inquiries | quotations | leases (QA_07: confirmed leases ending in
        /// the next 90 days, or month / from / to).
        /// </summary>
        [HttpGet("api/ceo/records/{kind}")]
        public Task<IActionResult> Records(string kind, [FromQuery] CeoRecordsQuery query) =>
            Run(() => _ceo.GetRecordsAsync(kind, query));

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
