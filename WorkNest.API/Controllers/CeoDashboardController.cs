using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
    /// Read-only, except for one network action: running a speed test on an internet line (api/ceo/network/speedtest).
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
        /// revenue takes from / to (billing period starting in that range, CS_01 / CP_01) instead of a month for a custom period.
        /// CP_02: occupancy-category (category = PrivateOffice | SharedSpace): rooms of the category with a confirmed booking now,
        /// by location; occupancy-location rows are per room category.
        /// </summary>
        [HttpGet("api/ceo/breakdown")]
        public Task<IActionResult> Breakdown([FromQuery] CeoBreakdownQuery query) =>
            Run(() => _ceo.GetBreakdownAsync(query));

        /// <summary>
        /// Level 3: records behind a breakdown row, newest first (leases: soonest first), at most 200 (total = all that
        /// match). kind = invoices | spaces | bookings | inquiries | quotations | leases (QA_07: confirmed leases ending in
        /// the next 90 days, or month / from / to). invoices: from / to = billing period starting in that range (CS_01 / CP_01);
        /// status = open | overdue | paid | advance (CP_01: unpaid, for a billing period not started yet) | collected (CP_02:
        /// something paid, the invoices behind Collected; before CP_02 the paid ones) | deposit (CP_02: with a security deposit).
        /// spaces (CP_02): category = PrivateOffice | SharedSpace, rooms = true (rooms only); occupied = a confirmed booking now.
        /// receipts (CP_02): money received (fee receipts and paid payments) by the day it came in: month, or from / to,
        /// else the period to date; the answer also has amount (the sum of every matching row).
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
        public async Task<IActionResult> Network([FromQuery] int? locationId, [FromServices] INetworkDashboardService network,
            [FromQuery] string? period = null, [FromQuery] string? from = null, [FromQuery] string? to = null, [FromServices] IBusinessClock? clock = null)
        {
            // period / from / to (as the dashboard call): the top data users over that period; none = today.
            var usage = NetworkUsageRange.FromPeriod(period, from, to, clock?.Today ?? PakistanNow().Date, clock?.Now ?? PakistanNow(), out var periodError);
            if (periodError != null) return BadRequest(ApiResponse.Fail(periodError));
            return Ok(ApiResponse.Ok(await network.GetAsync(new NetworkScope(null, locationId is > 0 ? locationId : null, usage),
                HttpContext?.RequestAborted ?? default)));
        }

        /// <summary>
        /// Runs a speed test on one internet line of the main console's gateway now (wan = WAN1 / WAN2 / ...), the same
        /// test as the Network Overview's "Run test" (api/unifi/speedtest, admins). The only network action the CEO has:
        /// it uses the line's full bandwidth for about 30 seconds. UniFi runs one test at a time: 409 while one is
        /// running (started here or on the Network pages), 400 for a line the gateway does not have, 502 when UniFi
        /// cannot be reached. Answers { wan, ifname, startedAt }; follow it with GET api/ceo/network/speedtest.
        /// </summary>
        [HttpPost("api/ceo/network/speedtest")]
        public Task<IActionResult> StartSpeedtest([FromQuery] string? wan, [FromServices] IUnifiService unifi)
        {
            if (string.IsNullOrWhiteSpace(wan) || !WanRe.IsMatch(wan))
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse.Fail("Choose the internet line to test (WAN1, WAN2, ...).")));
            return RunSpeedtest(() => unifi.StartSpeedtestAsync(wan.ToUpperInvariant()));
        }

        /// <summary>
        /// The running speed test: { running: false } when none, else { running, wan, elapsed (s), result: { down, up
        /// (Mbps), ping (ms), server, interface } once finished, timedOut }. 502 when UniFi cannot be reached.
        /// </summary>
        [HttpGet("api/ceo/network/speedtest")]
        public Task<IActionResult> SpeedtestStatus([FromServices] IUnifiService unifi) =>
            RunSpeedtest(() => unifi.GetSpeedtestStatusAsync());

        private static readonly Regex WanRe = new("^WAN[0-9]{1,2}$", RegexOptions.IgnoreCase);

        private async Task<IActionResult> RunSpeedtest(Func<Task<JsonObject>> action)
        {
            try
            {
                return Ok(ApiResponse.Ok(await action()));
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("A speed test is already running", StringComparison.Ordinal))
            {
                return Conflict(ApiResponse.Fail($"{ex.Message}. UniFi runs one test at a time: wait for it to finish (about 30 seconds)."));
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("Unknown WAN", StringComparison.Ordinal))
            {
                return BadRequest(ApiResponse.Fail("This line cannot be tested: the gateway does not report it."));
            }
            catch (Exception ex)
            {
                _logger.LogError("[ceo speedtest] {Error}", ex.Message);
                return StatusCode(StatusCodes.Status502BadGateway,
                    ApiResponse.Fail("The network could not be reached to run the speed test. Try again in a minute."));
            }
        }

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
        /// <summary>Pakistan wall-clock time (UTC+5), when no business clock is given.</summary>
        private static DateTime PakistanNow() => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(5), DateTimeKind.Unspecified);
    }
}
