using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.Gwn;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Grandstream GWN network pages (CMS /admin/network/gwn/*), contract Database/Gwn/GWN_CONTRACT.md. Same read roles as
    /// UnifiController. Location-bound staff (admin, sales executive) only see the networks linked to one of their
    /// locations in dbo.WN_GWN_NetworkLocations (403 otherwise); super admins see every network. Block / unblock, reboot,
    /// the action log and the network-location edit are super admin only. Answers { isSuccessful, message, data }.
    /// <c>net</c> = GWN network id (default: the first network the caller may see); <c>locationId</c> = the network of that
    /// location instead. GWN not set up / turned off: 200 with data { available: false, message } on every endpoint.
    /// GWN errors: 502 with a plain message.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive,network_engineer,NetworkEngineer")]
    [NetworkEngineerAllowed]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class GwnController : ControllerBase
    {
        public const string OtherLocationMessage = "The network belongs to another location.";
        public const string NoNetworkMessage = "No Grandstream network is linked to your locations.";
        public const string NetworkNotFoundMessage = "This Grandstream network was not found.";
        public const string NoNetworkForLocationMessage = "No Grandstream network is linked to this location.";
        public const string InvalidMacMessage = "Invalid MAC address.";

        private readonly IGwnService _gwn;
        private readonly ILogger<GwnController> _logger;

        public GwnController(IGwnService gwn, ILogger<GwnController> logger)
        {
            _gwn = gwn;
            _logger = logger;
        }

        // ---- networks ---------------------------------------------------------------------------------------------

        /// <summary>The GWN networks this user may see: [{ id, name, locationIds }].</summary>
        [HttpGet("api/gwn/networks")]
        public Task<IActionResult> GetNetworks() => Run(async () =>
            _gwn.UnavailableMessage is { } off ? Unavailable(off) : Ok(ApiResponse.Ok(await VisibleAsync())));

        /// <summary>Health, KPIs, devices, client counts, today's usage, traffic now, SSIDs and alerts of one network.</summary>
        [HttpGet("api/gwn/overview")]
        public Task<IActionResult> GetOverview([FromQuery] long? net = null, [FromQuery] int? locationId = null) =>
            WithNetwork(net, locationId, async n =>
            {
                try
                {
                    return Ok(ApiResponse.Ok(await _gwn.GetOverviewAsync(n, HttpContext?.RequestAborted ?? default)));
                }
                catch (GwnApiException ex)
                {
                    _logger.LogWarning("[gwn] overview of {Network}: {Error}", n.Id, ex.Message);
                    return Ok(ApiResponse.Ok(new GwnOverviewDto { Available = false, Message = ex.Message }));
                }
            });

        /// <summary>Connected clients.</summary>
        [HttpGet("api/gwn/clients")]
        public Task<IActionResult> GetClients([FromQuery] long? net = null, [FromQuery] int? locationId = null) =>
            WithNetwork(net, locationId, async n => Ok(ApiResponse.Ok(await _gwn.GetClientsAsync(n.Id))));

        /// <summary>Clients seen between from and to (epoch ms or ISO date-time; empty = open), most recent first.</summary>
        [HttpGet("api/gwn/clients/history")]
        public Task<IActionResult> GetClientHistory([FromQuery] long? net = null, [FromQuery] string? from = null, [FromQuery] string? to = null,
            [FromQuery] int? locationId = null) =>
            WithNetwork(net, locationId, async n =>
            {
                if (!TryTime(from, out var f) || !TryTime(to, out var t)) return BadRequest(ApiResponse.Fail("Give from / to as a date-time or epoch milliseconds."));
                return Ok(ApiResponse.Ok(await _gwn.GetClientHistoryAsync(n.Id, f, t)));
            });

        /// <summary>One client with GWN's own record (raw).</summary>
        [HttpGet("api/gwn/client")]
        public Task<IActionResult> GetClient([FromQuery] long? net = null, [FromQuery] string? mac = null, [FromQuery] int? locationId = null) =>
            WithNetwork(net, locationId, async n =>
            {
                if (!IsMac(mac)) return BadRequest(ApiResponse.Fail(InvalidMacMessage));
                var client = await _gwn.GetClientAsync(n.Id, mac!);
                return client == null ? NotFound(ApiResponse.Fail("Client not found in this network.")) : Ok(ApiResponse.Ok(client));
            });

        /// <summary>One client's traffic over range = 2h | 1d | 1w | 1m.</summary>
        [HttpGet("api/gwn/client/usage")]
        public Task<IActionResult> GetClientUsage([FromQuery] long? net = null, [FromQuery] string? mac = null, [FromQuery] string? range = null,
            [FromQuery] int? locationId = null) =>
            WithNetwork(net, locationId, async n =>
            {
                if (!IsMac(mac)) return BadRequest(ApiResponse.Fail(InvalidMacMessage));
                return Ok(ApiResponse.Ok(await _gwn.GetClientUsageAsync(n.Id, mac!, range)));
            });

        /// <summary>GWN devices (router, access points) with today's usage, temperature and memory.</summary>
        [HttpGet("api/gwn/devices")]
        public Task<IActionResult> GetDevices([FromQuery] long? net = null, [FromQuery] int? locationId = null) =>
            WithNetwork(net, locationId, async n => Ok(ApiResponse.Ok(await _gwn.GetDevicesAsync(n.Id))));

        /// <summary>One device: info rows, radios and its connected clients.</summary>
        [HttpGet("api/gwn/device")]
        public Task<IActionResult> GetDevice([FromQuery] long? net = null, [FromQuery] string? mac = null, [FromQuery] int? locationId = null) =>
            WithNetwork(net, locationId, async n =>
            {
                if (!IsMac(mac)) return BadRequest(ApiResponse.Fail(InvalidMacMessage));
                var device = await _gwn.GetDeviceAsync(n.Id, mac!);
                return device == null ? NotFound(ApiResponse.Fail("Device not found in this network.")) : Ok(ApiResponse.Ok(device));
            });

        /// <summary>One device's clients and traffic over range.</summary>
        [HttpGet("api/gwn/device/stats")]
        public Task<IActionResult> GetDeviceStats([FromQuery] long? net = null, [FromQuery] string? mac = null, [FromQuery] string? range = null,
            [FromQuery] int? locationId = null) =>
            WithNetwork(net, locationId, async n =>
            {
                if (!IsMac(mac)) return BadRequest(ApiResponse.Fail(InvalidMacMessage));
                return Ok(ApiResponse.Ok(await _gwn.GetDeviceStatsAsync(n.Id, mac!, range)));
            });

        /// <summary>Wi-Fi networks with their clients, bands and non-secret settings.</summary>
        [HttpGet("api/gwn/ssids")]
        public Task<IActionResult> GetSsids([FromQuery] long? net = null, [FromQuery] int? locationId = null) =>
            WithNetwork(net, locationId, async n => Ok(ApiResponse.Ok(await _gwn.GetSsidsAsync(n.Id))));

        /// <summary>Connected clients over range = 2h | 1d | 1w | 1m.</summary>
        [HttpGet("api/gwn/stats/clients")]
        public Task<IActionResult> GetClientStats([FromQuery] long? net = null, [FromQuery] string? range = null, [FromQuery] int? locationId = null) =>
            WithNetwork(net, locationId, async n => Ok(ApiResponse.Ok(await _gwn.GetClientStatsAsync(n.Id, range))));

        /// <summary>Network traffic over range (bytes per step and bits per second).</summary>
        [HttpGet("api/gwn/stats/bandwidth")]
        public Task<IActionResult> GetBandwidthStats([FromQuery] long? net = null, [FromQuery] string? range = null, [FromQuery] int? locationId = null) =>
            WithNetwork(net, locationId, async n => Ok(ApiResponse.Ok(await _gwn.GetBandwidthStatsAsync(n.Id, range))));

        /// <summary>Clients per SSID over range; ssid = one SSID id only.</summary>
        [HttpGet("api/gwn/stats/ssid")]
        public Task<IActionResult> GetSsidStats([FromQuery] long? net = null, [FromQuery] string? range = null, [FromQuery] string? ssid = null,
            [FromQuery] int? locationId = null) =>
            WithNetwork(net, locationId, async n => Ok(ApiResponse.Ok(await _gwn.GetSsidStatsAsync(n.Id, range, ssid))));

        // ---- changes ----------------------------------------------------------------------------------------------

        /// <summary>Renames a client: body { mac, name }; an empty name removes the alias. Admin / super admin.</summary>
        [HttpPost("api/gwn/alias")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,network_engineer,NetworkEngineer")]
        public Task<IActionResult> SetAlias([FromBody] JsonElement body) => Run(async () =>
        {
            if (_gwn.UnavailableMessage is { } off) return Unavailable(off);
            if ((await VisibleAsync()).Count == 0) return Forbidden(NoNetworkMessage);
            var result = await _gwn.SetAliasAsync(Field(body, "mac"), Field(body, "name"), Email());
            return result == null ? BadRequest(ApiResponse.Fail(InvalidMacMessage)) : Ok(ApiResponse.Ok(result));
        });

        /// <summary>
        /// Blocks / unblocks a client on GWN: body { net, mac, block: true | false }. Super admin only; every attempt is
        /// written to dbo.WN_GWN_ActionLog.
        /// </summary>
        [HttpPost("api/gwn/client/block")]
        [Authorize(Roles = "super_admin,SuperAdmin,network_engineer,NetworkEngineer")]
        public Task<IActionResult> BlockClient([FromBody] JsonElement body) =>
            WithNetwork(LongField(body, "net"), IntField(body, "locationId"), async n =>
            {
                var mac = Field(body, "mac");
                if (!IsMac(mac)) return BadRequest(ApiResponse.Fail(InvalidMacMessage));
                if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("block", out var b) || b.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return BadRequest(ApiResponse.Fail("Say whether the client should be blocked (true or false)."));
                return Ok(ApiResponse.Ok(await _gwn.BlockClientAsync(n.Id, mac!, b.GetBoolean(), Email(), UserId())));
            });

        /// <summary>Reboots a GWN device of the network: body { net, mac }. Super admin only; logged in dbo.WN_GWN_ActionLog.</summary>
        [HttpPost("api/gwn/device/reboot")]
        [Authorize(Roles = "super_admin,SuperAdmin,network_engineer,NetworkEngineer")]
        public Task<IActionResult> RebootDevice([FromBody] JsonElement body) =>
            WithNetwork(LongField(body, "net"), IntField(body, "locationId"), async n =>
            {
                var mac = Field(body, "mac");
                if (!IsMac(mac)) return BadRequest(ApiResponse.Fail(InvalidMacMessage));
                var result = await _gwn.RebootDeviceAsync(n.Id, mac!, Email(), UserId());
                return result == null ? NotFound(ApiResponse.Fail("Device not found in this network.")) : Ok(ApiResponse.Ok(result));
            });

        /// <summary>Latest block / unblock / reboot actions (net = one network; top 1–500, default 50). Super admin and network engineers.</summary>
        [HttpGet("api/gwn/actions")]
        [Authorize(Roles = "super_admin,SuperAdmin,network_engineer,NetworkEngineer")]
        public Task<IActionResult> GetActions([FromQuery] long? net = null, [FromQuery] int? top = null) => Run(async () =>
        {
            var mine = MyLocations();
            if (mine != null)
            {
                var visible = await VisibleAsync();
                if (net is > 0)
                {
                    if (!visible.Any(n => n.Id == net)) return Forbidden(OtherLocationMessage);
                }
                else
                {
                    if (visible.Count == 0) return Forbidden(NoNetworkMessage);
                }
            }
            else if (!User.IsSuperAdmin())
            {
                return Forbidden("Only a super admin can see the Grandstream action log.");
            }
            return Ok(ApiResponse.Ok(await _gwn.GetActionsAsync(net is > 0 ? net : null, top is > 0 ? Math.Min(top.Value, 500) : 50)));
        });

        /// <summary>Each GWN network this user may see and its locations ({ available, canEdit, networks }).</summary>
        [HttpGet("api/gwn/network-locations")]
        public Task<IActionResult> GetNetworkLocations() => Run(async () =>
        {
            if (_gwn.UnavailableMessage is { } off) return Unavailable(off);
            var all = await _gwn.GetNetworkLocationsAsync(User.IsSuperAdmin());
            all.Networks = GwnNetworkScope.Visible(all.Networks, MyLocations(), null);
            return Ok(ApiResponse.Ok(all));
        });

        /// <summary>Saves the locations of one network: body { locationIds: [2] } (empty = none). Super admin only.</summary>
        [HttpPut("api/gwn/network-locations/{networkId:long}")]
        [Authorize(Roles = "super_admin,SuperAdmin")]
        public Task<IActionResult> SetNetworkLocations(long networkId, [FromBody] JsonElement body) => Run(async () =>
        {
            if (!User.IsSuperAdmin()) return Forbidden("Only a super admin can link a network to locations.");
            if (_gwn.UnavailableMessage is { } off) return Unavailable(off);
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("locationIds", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return BadRequest(ApiResponse.Fail("Choose the locations the network belongs to."));
            var ids = arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var v) && v > 0)
                .Select(x => x.GetInt32()).Distinct().Take(100).ToList();
            try
            {
                var saved = await _gwn.SetNetworkLocationsAsync(networkId, ids, Email());
                return saved == null ? NotFound(ApiResponse.Fail(NetworkNotFoundMessage)) : Ok(ApiResponse.Ok(saved));
            }
            catch (GwnTablesMissingException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(ex.Message));
            }
        });

        // ---- helpers ----------------------------------------------------------------------------------------------

        /// <summary>The caller's locations; null = every location (not location-bound: super admin).</summary>
        private List<int>? MyLocations() => User.IsLocationBoundRole() ? User.GetLocationIds() : null;

        private async Task<List<GwnNetworkDto>> VisibleAsync() =>
            GwnNetworkScope.Visible(await _gwn.GetNetworksAsync(HttpContext?.RequestAborted ?? default), MyLocations(), null);

        /// <summary>
        /// Resolves the network asked for (net, else the location's network, else the first one the caller may see) and
        /// runs the action with it; 403 for a network of another location, 404 for one that does not exist.
        /// </summary>
        private Task<IActionResult> WithNetwork(long? net, int? locationId, Func<GwnNetworkDto, Task<IActionResult>> action, bool superAdminOnly = false) => Run(async () =>
        {
            if (superAdminOnly && !User.IsSuperAdmin()) return Forbidden("Only a super admin can do this.");
            if (_gwn.UnavailableMessage is { } off) return Unavailable(off);
            var all = await _gwn.GetNetworksAsync(HttpContext?.RequestAborted ?? default);
            var mine = MyLocations();
            var visible = GwnNetworkScope.Visible(all, mine, null);
            GwnNetworkDto? network;
            if (net is > 0)
            {
                network = visible.FirstOrDefault(n => n.Id == net);
                if (network == null)
                    return all.Any(n => n.Id == net) || mine != null ? Forbidden(OtherLocationMessage) : NotFound(ApiResponse.Fail(NetworkNotFoundMessage));
            }
            else if (locationId is > 0)
            {
                if (mine != null && !mine.Contains(locationId.Value)) return Forbidden(OtherLocationMessage);
                network = GwnNetworkScope.Visible(all, mine, locationId).FirstOrDefault();
                if (network == null) return NotFound(ApiResponse.Fail(NoNetworkForLocationMessage));
            }
            else
            {
                network = visible.FirstOrDefault();
                if (network == null) return mine != null ? Forbidden(NoNetworkMessage) : NotFound(ApiResponse.Fail(NetworkNotFoundMessage));
            }
            return await action(network);
        });

        private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (GwnApiException ex)
            {
                _logger.LogError("[gwn] {Path}: {Error}", HttpContext?.Request.Path.Value, ex.Message);
                return StatusCode(StatusCodes.Status502BadGateway, ApiResponse.Fail(ex.Message));
            }
            catch (GwnTablesMissingException ex)
            {
                _logger.LogWarning("[gwn] {Path}: {Error}", HttpContext?.Request.Path.Value, ex.InnerException?.Message ?? ex.Message);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(ex.Message));
            }
        }

        private IActionResult Unavailable(string message) =>
            Ok(ApiResponse.Ok(new GwnUnavailableDto { Available = false, Message = message }, message));

        private ObjectResult Forbidden(string message) => StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail(message));

        private string? Email() => User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;

        private int? UserId()
        {
            var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("id")?.Value;
            return int.TryParse(id, out var n) ? n : null;
        }

        private static bool IsMac(string? mac) => !string.IsNullOrWhiteSpace(mac) && mac.Length <= 40
            && System.Text.RegularExpressions.Regex.IsMatch(mac, "^[0-9a-fA-F:.-]+$") && WorkNest.Application.Services.GwnService.IsMac(mac);

        /// <summary>Epoch ms or an ISO date-time → UTC; empty = null (true). False when it cannot be read.</summary>
        private static bool TryTime(string? value, out DateTime? utc)
        {
            utc = null;
            if (string.IsNullOrWhiteSpace(value)) return true;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
            {
                if (ms <= 0) return true;
                utc = DateTime.UnixEpoch.AddMilliseconds(Math.Min(ms, 253402300799000));
                return true;
            }
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
            {
                utc = d.UtcDateTime;
                return true;
            }
            return false;
        }

        private static string? Field(JsonElement body, string name)
        {
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var v)) return null;
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null
            };
        }

        private static long? LongField(JsonElement body, string name) =>
            long.TryParse(Field(body, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;

        private static int? IntField(JsonElement body, string name) =>
            int.TryParse(Field(body, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
    }
}
