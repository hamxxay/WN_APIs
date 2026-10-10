using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.Interfaces;
using WorkNest.API.Filters;
using WorkNest.API.Extensions;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// UniFi network dashboard. Same endpoints and JSON as "Unifi UI" server.js, under api/unifi/
    /// (e.g. /api/summary → api/unifi/summary). Console errors return 502 { error }.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [TypeFilter(typeof(UnifiLocationScopeFilter))] // admins / sales executives of other locations get 403
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

        /// <summary>
        /// Every SSID with its MAC filter (off / allow-list / block-list), the MACs in it and the clients connected
        /// to it right now.
        /// </summary>
        [HttpGet("api/unifi/ssids")]
        public Task<IActionResult> GetSsids() => Run(() => _unifi.GetSsidsAsync());

        /// <summary>
        /// Changes one SSID's MAC filter: body { action: add | remove | block | unblock | mode, mac, name, roomNo, policy, macs, reason }.
        /// block / unblock act on this SSID only; mode sets allow | deny | off. Logged in WN_UNIFI_MacFilterLog.
        /// All staff roles may use it.
        /// </summary>
        [HttpPost("api/unifi/ssids/{wlanId}/mac-filter")]
        public Task<IActionResult> UpdateMacFilter(string wlanId, [FromBody] JsonElement body) => Run(async () =>
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("id")?.Value;
            int? userId = int.TryParse(idClaim, out var uid) ? uid : null;
            var macs = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("macs", out var m) && m.ValueKind == JsonValueKind.Array
                ? m.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Take(500).ToList()
                : null;
            var result = await _unifi.UpdateMacFilterAsync(wlanId, Field(body, "action") ?? "", Field(body, "mac"),
                Field(body, "policy"), Field(body, "reason"), email, userId, macs, Field(body, "name"), Field(body, "roomNo"));
            return result.ContainsKey("error") ? BadRequest(result) : Ok(result);
        });

        /// <summary>Shows or hides the SSID name: body { hidden: true | false, reason }. All staff roles; logged.</summary>
        [HttpPost("api/unifi/ssids/{wlanId}/visibility")]
        public Task<IActionResult> SetSsidVisibility(string wlanId, [FromBody] JsonElement body) => Run(async () =>
        {
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("hidden", out var h) || h.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return BadRequest(new { error = "Say whether the SSID should be hidden (true or false)." });
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("id")?.Value;
            int? userId = int.TryParse(idClaim, out var uid) ? uid : null;
            var result = await _unifi.SetSsidHiddenAsync(wlanId, h.GetBoolean(), Field(body, "reason"), email, userId);
            return result.ContainsKey("error") ? BadRequest(result) : Ok(result);
        });

        /// <summary>UniFi speed profiles with their per-device speeds and the SSIDs using them.</summary>
        [HttpGet("api/unifi/speed-profiles")]
        public Task<IActionResult> GetSpeedProfiles() => Run(() => _unifi.GetSpeedProfilesAsync());

        /// <summary>Creates a speed profile: body { name, downMbps, upMbps }. Super admin only.</summary>
        [HttpPost("api/unifi/speed-profiles")]
        [Authorize(Roles = "super_admin,SuperAdmin")]
        public Task<IActionResult> CreateSpeedProfile([FromBody] JsonElement body) => Run(async () =>
        {
            var result = await _unifi.CreateSpeedProfileAsync(Field(body, "name"), IntField(body, "downMbps"), IntField(body, "upMbps"));
            return result.ContainsKey("error") ? BadRequest(result) : Ok(result);
        });

        /// <summary>
        /// Puts the SSID on a speed profile: body { profileId, reason }; no profileId = no limit (Default).
        /// Super admin only; logged.
        /// </summary>
        [HttpPost("api/unifi/ssids/{wlanId}/speed-profile")]
        [Authorize(Roles = "super_admin,SuperAdmin")]
        public Task<IActionResult> SetSsidSpeedProfile(string wlanId, [FromBody] JsonElement body) => Run(async () =>
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("id")?.Value;
            int? userId = int.TryParse(idClaim, out var uid) ? uid : null;
            var result = await _unifi.SetSsidSpeedProfileAsync(wlanId, Field(body, "profileId"), Field(body, "reason"), email, userId);
            return result.ContainsKey("error") ? BadRequest(result) : Ok(result);
        });

        /// <summary>
        /// Which locations the UniFi network belongs to, and whether this user may see it ({ locationIds, allowed, canEdit }).
        /// Any staff member may ask (the sidebar uses it to hide the Network section).
        /// </summary>
        [HttpGet("api/unifi/scope")]
        [AnyUnifiLocation]
        public async Task<IActionResult> GetScope() => Ok(new
        {
            locationIds = await _unifi.GetLocationIdsAsync(),
            allowed = await UnifiScope.AllowsAsync(_unifi, User),
            canEdit = User.IsSuperAdmin()
        });

        /// <summary>Saves the locations the UniFi network belongs to: body { locationIds: [1] }; empty = every location. Super admin only.</summary>
        [HttpPut("api/unifi/scope")]
        [AnyUnifiLocation]
        [Authorize(Roles = "super_admin,SuperAdmin")]
        public async Task<IActionResult> SetScope([FromBody] JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("locationIds", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return BadRequest(new { error = "Choose the locations the network belongs to." });
            var ids = arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _)).Select(x => x.GetInt32()).ToList();
            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
            if (!await _unifi.SetLocationIdsAsync(ids, email))
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The setting could not be saved. Run WN_UNIFI_Settings.txt to create its table." });
            return Ok(new { locationIds = await _unifi.GetLocationIdsAsync(), allowed = true, canEdit = true });
        }

        /// <summary>Latest MAC filter changes (who, what, when, why), optionally for one SSID (wlanId) or one MAC.</summary>
        [HttpGet("api/unifi/ssids/log")]
        public Task<IActionResult> GetMacFilterLog([FromQuery] string? wlanId = null, [FromQuery] string? mac = null) =>
            Run(() => _unifi.GetMacFilterLogAsync(wlanId, mac));

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

        private static int? IntField(JsonElement body, string name) =>
            body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

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
