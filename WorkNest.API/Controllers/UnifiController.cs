using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// UniFi network dashboard. Same endpoints and JSON as "Unifi UI" server.js, under api/unifi/
    /// (e.g. /api/summary → api/unifi/summary). Console errors return 502 { error }.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class UnifiController : ControllerBase
    {
        private static readonly Regex DeviceMacRe = new("^[0-9a-fA-F:.-]+$");

        private readonly IUnifiService _unifi;
        private readonly ILogger<UnifiController> _logger;

        public UnifiController(IUnifiService unifi, ILogger<UnifiController> logger)
        {
            _unifi = unifi;
            _logger = logger;
        }

        /// <summary>
        /// Sites, devices, ISP metrics, client-count history and alerts (from the last poll).
        /// </summary>
        [HttpGet("api/unifi/summary")]
        public async Task<IActionResult> GetSummary()
        {
            return Ok(await _unifi.GetSummaryAsync());
        }

        /// <summary>
        /// Polls the cloud API now, then returns the summary.
        /// </summary>
        [HttpPost("api/unifi/refresh")]
        public async Task<IActionResult> Refresh()
        {
            return Ok(await _unifi.RefreshAsync());
        }

        /// <summary>
        /// Connected clients plus the UniFi device list (mac, name, type).
        /// </summary>
        [HttpGet("api/unifi/clients")]
        public Task<IActionResult> GetClients() => Run(() => _unifi.GetClientsAsync());

        /// <summary>
        /// Network topology nodes (UniFi devices + third-party switches).
        /// </summary>
        [HttpGet("api/unifi/topology")]
        public Task<IActionResult> GetTopology() => Run(() => _unifi.GetTopologyAsync());

        /// <summary>
        /// Live WAN ports on the gateway: link, rates, latency, last speed test, 24h traffic.
        /// </summary>
        [HttpGet("api/unifi/wans")]
        public Task<IActionResult> GetWans() => Run(() => _unifi.GetWansAsync());

        /// <summary>
        /// ISP comparison, speed-test history and failover episodes.
        /// </summary>
        [HttpGet("api/unifi/isp")]
        public Task<IActionResult> GetIsp() => Run(() => _unifi.GetIspAsync());

        /// <summary>
        /// Wi-Fi health per access point and radio, with 24h hourly history.
        /// </summary>
        [HttpGet("api/unifi/wifi")]
        public Task<IActionResult> GetWifi() => Run(() => _unifi.GetWifiAsync());

        /// <summary>
        /// Daily (30 days) and hourly (48h) data usage of one client.
        /// </summary>
        [HttpGet("api/unifi/client-usage")]
        public Task<IActionResult> GetClientUsage([FromQuery] string? mac = null) => Run(() => _unifi.GetClientUsageAsync(mac));

        /// <summary>
        /// Top 50 clients by data used over the last N days (1–30, default 7).
        /// </summary>
        [HttpGet("api/unifi/usage")]
        public Task<IActionResult> GetUsage([FromQuery] string? days = null) =>
            Run(() => _unifi.GetTopUsageAsync(Clamp(days, 1, 30, 7)));

        /// <summary>
        /// Starts a speed test on one WAN port (wan=WAN1/WAN2/...).
        /// </summary>
        [HttpPost("api/unifi/speedtest")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")] // uses the line's full bandwidth for ~30s
        public Task<IActionResult> StartSpeedtest([FromQuery] string? wan = null) => Run(() => _unifi.StartSpeedtestAsync(wan));

        /// <summary>
        /// Status / result of the running speed test.
        /// </summary>
        [HttpGet("api/unifi/speedtest")]
        public Task<IActionResult> GetSpeedtest() => Run(() => _unifi.GetSpeedtestStatusAsync());

        /// <summary>
        /// Gateway WAN traffic history for the last N hours (1–168, default 24).
        /// </summary>
        [HttpGet("api/unifi/wan-traffic")]
        public Task<IActionResult> GetWanTraffic([FromQuery] string? range = null) =>
            Run(() => _unifi.GetWanTrafficAsync(Clamp(range, 1, 168, 24)));

        /// <summary>
        /// Dashboard branding (brand name; logo is always empty).
        /// </summary>
        [HttpGet("api/unifi/config")]
        public Task<IActionResult> GetConfig() => Run(() => _unifi.GetConfigAsync());

        /// <summary>
        /// Renames a client: body { mac, name }; an empty name restores the UniFi name. Admin / super admin only.
        /// </summary>
        [HttpPost("api/unifi/alias")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
        public Task<IActionResult> SetAlias([FromBody] JsonElement body) => Run(async () =>
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
            var result = await _unifi.SetAliasAsync(Field(body, "mac"), Field(body, "name"), email);
            return result == null ? BadRequest(new { error = "Invalid MAC" }) : Ok(result);
        });

        /// <summary>
        /// Console event log (last 24h + 30 days of device alerts), optionally for one MAC; max 500.
        /// </summary>
        [HttpGet("api/unifi/logs")]
        public Task<IActionResult> GetLogs([FromQuery] string? mac = null) => Run(() => _unifi.GetLogsAsync(mac));

        /// <summary>
        /// One device: live detail, its connected clients and its logs.
        /// </summary>
        [HttpGet("api/unifi/device/{mac}")]
        public Task<IActionResult> GetDevice(string mac) => Run(async () =>
        {
            if (!DeviceMacRe.IsMatch(mac)) return NotFound(new { error = "Unknown endpoint" });
            var result = await _unifi.GetDeviceAsync(mac);
            return result == null ? NotFound(new { error = "Device not found on the console" }) : Ok(result);
        });

        /// <summary>
        /// One device's traffic / clients / CPU / memory history for the last N hours (1–168, default 24).
        /// </summary>
        [HttpGet("api/unifi/device/{mac}/history")]
        public Task<IActionResult> GetDeviceHistory(string mac, [FromQuery] string? range = null) => Run(async () =>
        {
            if (!DeviceMacRe.IsMatch(mac)) return NotFound(new { error = "Unknown endpoint" });
            var result = await _unifi.GetDeviceHistoryAsync(mac, Clamp(range, 1, 168, 24));
            return result == null ? NotFound(new { error = "Device not found" }) : Ok(result);
        });

        // ---- Helpers --------------------------------------------------------------

        private Task<IActionResult> Run<T>(Func<Task<T>> action) => Run(async () => (IActionResult)Ok(await action()));

        // Console / cloud failures are returned as 502 { error } like the Node server.
        private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                _logger.LogError("[unifi net] {Error}", ex.Message);
                return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
            }
        }

        // Math.min(max, Math.max(min, Number(value) || fallback))
        private static int Clamp(string? value, int min, int max, int fallback)
        {
            var n = double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v != 0 && !double.IsNaN(v) ? v : fallback;
            return (int)Math.Min(max, Math.Max(min, n));
        }

        private static string? Field(JsonElement body, string name)
        {
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var v)) return null;
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.True => "true",
                _ => null,
            };
        }
    }
}
