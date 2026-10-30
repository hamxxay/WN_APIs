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
    /// locations a Super Admin assigned (JWT location_id + location_ids, like an admin); tour inquiries by their branch
    /// (BR_02), plus the ones without a branch when no locationId is chosen (as complaints). Super admins may open them
    /// too (all locations). Common query parameters:
    /// period = month (default) | quarter | year (to date, against the previous period, as the CEO dashboard) | custom
    /// (CS_01: a day range, against the same number of days just before it; dashboard and complaints: from / to,
    /// breakdown and records: periodFrom / periodTo, yyyy-MM-dd, at most 366 days, not after today) and
    /// locationId (one of the caller's locations; another location answers 403). No money: counts and desks only.
    /// Bad parameters answer 400; before the database update SM_01 (procedures) or SM_02 (desks) has run, 503; the
    /// complaints endpoints answer 503 until SM_03 has run (the rest keeps working); a custom period answers 503 until
    /// CS_01 has run (month / quarter / year keep working).
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

        /// <summary>Level 1: sales funnel, team, tour inquiries, renewals and desks, plus the locations for the picker.</summary>
        [HttpGet("api/sales-manager/dashboard")]
        public Task<IActionResult> Dashboard([FromQuery] string? period, [FromQuery] int? locationId,
            [FromQuery] string? from = null, [FromQuery] string? to = null) =>
            Run(locationId, false, scope => _sales.GetDashboardAsync(period, scope, from, to));

        /// <summary>
        /// Level 2: one chart element broken down. metric = funnel-month (month) | staff (userId) | inquiries-type (type)
        /// | renewals-month (month) | desks-month (month).
        /// </summary>
        [HttpGet("api/sales-manager/breakdown")]
        public Task<IActionResult> Breakdown([FromQuery] SalesManagerBreakdownQuery query) =>
            Run(query.LocationId, false, scope => _sales.GetBreakdownAsync(query, scope));

        /// <summary>
        /// Level 3: records behind a breakdown row, newest first (leases: soonest first), at most 200 (total = all that
        /// match). kind = quotations | bookings | inquiries | leases. Inquiries: the chosen location's; without locationId
        /// the caller's locations' plus those without a branch; locationId=none (super admins): only those without one.
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
        /// Complaints section: complaints by status right now, new / resolved / reopened in the period, by category and
        /// branch, the last 12 months and the oldest open ones. Complaints have no location: a complaint belongs to the
        /// location named in its branch; the ones without a branch (WhatsApp) count only when no locationId is chosen.
        /// </summary>
        [HttpGet("api/sales-manager/complaints")]
        public Task<IActionResult> Complaints([FromQuery] string? period, [FromQuery] int? locationId,
            [FromQuery] string? from = null, [FromQuery] string? to = null) =>
            Run(locationId, false, scope => _sales.GetComplaintsAsync(period, scope, from, to));

        /// <summary>
        /// Complaint records, newest first, at most 200 (total = all that match). status = open | in_progress | resolved
        /// | closed | active | resolved_period | new_period | reopened_period; category ("Other" also = none); branch
        /// ("none" = no branch); month (yyyy-MM, created in it); period (for the *_period statuses; custom: periodFrom /
        /// periodTo); locationId.
        /// </summary>
        [HttpGet("api/sales-manager/records/complaints")]
        public Task<IActionResult> ComplaintRecords([FromQuery] SalesManagerComplaintRecordsQuery query)
        {
            var text = (query.LocationId ?? "").Trim();
            if (text.Length > 0 && !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse.Fail("locationId must be a location id.")));
            int? locationId = text.Length == 0 ? null : int.Parse(text, CultureInfo.InvariantCulture);
            return Run(locationId, false, scope => _sales.GetComplaintRecordsAsync(query, scope));
        }

        /// <summary>
        /// Network (UniFi) section: the same figures as the CEO dashboard's, for the UniFi sites that serve the manager's
        /// locations (or the one chosen; another location answers 403, as everywhere here). Super admins: every site.
        /// Read-only; UniFi not set up or not reachable: 200 with available = false and a short message.
        /// </summary>
        [HttpGet("api/sales-manager/network")]
        public async Task<IActionResult> Network([FromQuery] int? locationId, [FromServices] INetworkDashboardService network,
            [FromQuery] string? period = null, [FromQuery] string? from = null, [FromQuery] string? to = null, [FromServices] IBusinessClock? clock = null)
        {
            var scope = Scope(locationId, false, out var refusal);
            if (scope == null) return refusal!;
            // period / from / to (as the dashboard call): the top data users over that period; none = today.
            var usage = NetworkUsageRange.FromPeriod(period, from, to, clock?.Today ?? PakistanNow().Date, clock?.Now ?? PakistanNow(), out var periodError);
            if (periodError != null) return BadRequest(ApiResponse.Fail(periodError));
            return Ok(ApiResponse.Ok(await network.GetAsync(new NetworkScope(scope.MyLocations, scope.LocationId, usage),
                HttpContext?.RequestAborted ?? default)));
        }

        /// <summary>
        /// Room map and pricing against list for the sales manager's locations.
        /// </summary>
        [HttpGet("api/sales-manager/rooms")]
        public Task<IActionResult> Rooms([FromQuery] string? period, [FromQuery] int? locationId,
            [FromQuery] string? from = null, [FromQuery] string? to = null,
            [FromServices] ICeoDashboardService ceo = null!) =>
            Run(locationId, false, scope =>
                ceo.GetRoomReportAsync(period, scope.LocationId, from, to, scope.LocationIdsCsv));

        /// <summary>
        /// Invoiced, receivable and overdue figures for the sales manager's locations.
        /// </summary>
        [HttpGet("api/sales-manager/money")]
        public Task<IActionResult> Money([FromQuery] string? period, [FromQuery] int? locationId,
            [FromQuery] string? from = null, [FromQuery] string? to = null,
            [FromServices] ICeoDashboardService ceo = null!) =>
            Run(locationId, false, async scope =>
            {
                var res = await ceo.GetDashboardAsync(period, scope.LocationId, from, to, scope.LocationIdsCsv);
                if (!res.IsSuccessful || res.Data is not CeoDashboardDto dto) return res;
                return ApiResponse.Ok(new SalesManagerMoneyResponseDto
                {
                    Period = dto.Period,
                    PeriodLabel = dto.PeriodLabel,
                    From = dto.From,
                    To = dto.To,
                    Money = dto.Money
                });
            });

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
                _logger.LogWarning("Sales Manager dashboard called before the database update {Update}: {Error}", ex.Update, ex.Detail);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(ex.Message));
            }
        }
        /// <summary>Pakistan wall-clock time (UTC+5), when no business clock is given.</summary>
        private static DateTime PakistanNow() => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(5), DateTimeKind.Unspecified);
    }
}
