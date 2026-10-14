using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Sales Manager dashboard: read-only. A Sales Manager (role sales_manager) sees only these endpoints, limited to the
    /// locations a Super Admin assigned (JWT location_id + location_ids, like an admin); tour inquiries have no location
    /// and are always all of them. Super admins may open them too (all locations). Common query parameters:
    /// period = month (default) | quarter | year (to date, against the previous period, as the CEO dashboard) and
    /// locationId (one of the caller's locations; another location answers 403). Bad parameters answer 400; before
    /// the database update SM_01 has run, 503.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "sales_manager,super_admin,SuperAdmin")]
    [SalesManagerAllowed]
    public class SalesManagerController : ControllerBase
    {
        private readonly ISalesManagerDashboardService _sales;
        private readonly ILogger<SalesManagerController> _logger;

        public SalesManagerController(ISalesManagerDashboardService sales, ILogger<SalesManagerController> logger)
        {
            _sales = sales;
            _logger = logger;
        }

        /// <summary>Level 1: sales funnel, team, tour inquiries, renewals and revenue, plus the locations for the picker.</summary>
        [HttpGet("api/sales-manager/dashboard")]
        public Task<IActionResult> Dashboard([FromQuery] string? period, [FromQuery] int? locationId) =>
            Run(locationId, false, scope => _sales.GetDashboardAsync(period, scope));

        /// <summary>
        /// Level 2: one chart element broken down. metric = funnel-month (month) | staff (userId) | inquiries-type (type)
        /// | renewals-month (month) | revenue-month (month).
        /// </summary>
        [HttpGet("api/sales-manager/breakdown")]
        public Task<IActionResult> Breakdown([FromQuery] SalesManagerBreakdownQuery query) =>
            Run(query.LocationId, false, scope => _sales.GetBreakdownAsync(query, scope));

        /// <summary>
        /// Level 3: records behind a breakdown row, newest first (leases: soonest first), at most 200 (total = all that
        /// match). kind = quotations | bookings | inquiries | leases.
        /// </summary>
        [HttpGet("api/sales-manager/records/{kind}")]
        public Task<IActionResult> Records(string kind, [FromQuery] SalesManagerRecordsQuery query)
        {
            var text = (query.LocationId ?? "").Trim();
            if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
                return Run(null, true, scope => _sales.GetRecordsAsync(kind, query, scope));
            if (text.Length > 0 && !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse.Fail("locationId must be a location id.")));
            int? locationId = text.Length == 0 ? null : int.Parse(text, CultureInfo.InvariantCulture);
            return Run(locationId, false, scope => _sales.GetRecordsAsync(kind, query, scope));
        }

        /// <summary>
        /// The caller's scope: a Sales Manager covers their own locations (403 for any other, or when they have none);
        /// a super admin all locations, or the one asked for. Null (with <paramref name="refusal"/>) when refused.
        /// </summary>
        private SalesManagerScope? Scope(int? locationId, bool noLocation, out IActionResult? refusal)
        {
            refusal = null;
            var requested = locationId is > 0 ? locationId : null;
            if (!User.IsSalesManager())
                return new SalesManagerScope(null, noLocation ? null : requested, noLocation);

            var mine = User.GetLocationIds();
            if (mine.Count == 0)
                refusal = Forbidden("Your account has no location yet. Ask a Super Admin to assign your locations.");
            else if (noLocation)
                refusal = Forbidden("Records not tied to a location are not part of your locations.");
            else if (requested != null && !mine.Contains(requested.Value))
                refusal = Forbidden($"Location mismatch. You aren't assigned to Location {requested.Value}.");
            return refusal == null ? new SalesManagerScope(mine, requested) : null;
        }

        private static IActionResult Forbidden(string message) =>
            new ObjectResult(ApiResponse.Fail(message)) { StatusCode = StatusCodes.Status403Forbidden };

        private async Task<IActionResult> Run(int? locationId, bool noLocation, Func<SalesManagerScope, Task<ApiResponse>> action)
        {
            var scope = Scope(locationId, noLocation, out var refusal);
            if (scope == null) return refusal!;
            try
            {
                var result = await action(scope);
                return result.IsSuccessful ? Ok(result) : BadRequest(result);
            }
            catch (SalesManagerForbiddenException ex)
            {
                return Forbidden(ex.Message);
            }
            catch (SalesManagerDatabaseUpdateMissingException ex)
            {
                _logger.LogWarning("Sales Manager dashboard called before the database update SM_01: {Error}", ex.InnerException?.Message);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(SalesManagerDatabaseUpdateMissingException.UserMessage));
            }
        }
    }
}
