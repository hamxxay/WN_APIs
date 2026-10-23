using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.DTOs.Gwn;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Grandstream GWN network — port of WN_Grandstream/server.js (same GWN calls, units and quirks). Singleton. Keeps a
    /// stale-while-revalidate cache of what it reads (networks 10 min, network detail 1 h, devices / clients / SSIDs every
    /// Gwn:RefreshSeconds, statistics 5 min); GwnPollingService keeps it warm, so pages and dashboards rarely wait on GWN.
    /// Aliases, network locations and the action log live in dbo.WN_GWN_* (script GWN_01_grandstream); without those
    /// tables the pages still work (no aliases, no network linked to a location).
    /// GWN quirks: statistics times are UTC "yyyy-MM-dd HH:mm"; bandwidth arrays are bits per second; client firstSeen /
    /// lastSeen are network-local "yyyy/MM/dd hh:mmAM"; a router's `ip` is its WAN address and `ipv4` its LAN address;
    /// client `block` 0 = blocked, 1 = not blocked.
    /// </summary>
    public sealed class GwnService : IGwnService
    {
        public const string NotSetUpMessage = "Grandstream is not set up on this server.";
        public const string TurnedOffMessage = "Grandstream is turned off on this server.";
        public const string ReadFailedMessage = "The Grandstream network could not be reached. Its figures show here once it answers.";
        public const string NoDataYetMessage = "No Grandstream figures yet. They show a few minutes after the server starts.";

        private static readonly TimeSpan StatsTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan NetworksTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DetailTtl = TimeSpan.FromHours(1);
        private static readonly TimeSpan LocationsTtl = TimeSpan.FromSeconds(30);
        /// <summary>How long a dashboard waits for GWN before showing "no figures yet".</summary>
        private static readonly TimeSpan DashboardWait = TimeSpan.FromSeconds(8);

        private readonly IGwnClient _client;
        private readonly IServiceScopeFactory? _scopeFactory;
        private readonly IGwnRepository? _repository;
        private readonly ILogger<GwnService> _logger;
        private readonly GwnSettings _settings;
        private readonly TimeSpan _coreTtl;

        private ConcurrentDictionary<string, string> _aliases = new(); // key: 12 hex digits, lower case
        private DateTime _aliasesAt = DateTime.MinValue;

        public GwnService(IGwnClient client, IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<GwnService> logger)
            : this(client, configuration, logger, scopeFactory, null) { }

        /// <summary>For tests: a repository instead of the scope factory.</summary>
        public GwnService(IGwnClient client, IConfiguration configuration, ILogger<GwnService> logger, IServiceScopeFactory? scopeFactory, IGwnRepository? repository)
        {
            _client = client;
            _scopeFactory = scopeFactory;
            _repository = repository;
            _logger = logger;
            _settings = GwnSettings.From(configuration);
            _coreTtl = TimeSpan.FromSeconds(_settings.RefreshSeconds);
        }

        public bool IsConfigured => _client.IsConfigured;
        public bool IsEnabled => _settings.Enabled;
        public int RefreshSeconds => _settings.RefreshSeconds;
        public string? UnavailableMessage => !IsConfigured ? NotSetUpMessage : !IsEnabled ? TurnedOffMessage : null;

        // =====================================================================
        // Cache (stale-while-revalidate, like cached() in server.js)
        // =====================================================================

        private sealed class CacheEntry
        {
            public DateTime At;
            public object? Value;
            public bool HasValue;
            public Task? Pending;
        }

        private readonly Dictionary<string, CacheEntry> _cache = new();

        /// <summary>
        /// A fresh value is returned as is; a stale one is returned at once while it is read again in the background
        /// (unless <paramref name="wait"/>); with none, the caller waits for the read. Concurrent callers share one read.
        /// </summary>
        private Task<T> Cached<T>(string key, TimeSpan ttl, Func<Task<T>> read, bool wait = false)
        {
            lock (_cache)
            {
                _cache.TryGetValue(key, out var e);
                if (!wait && e is { HasValue: true } && DateTime.UtcNow - e.At < ttl) return Task.FromResult((T)e.Value!);
                if (e == null)
                {
                    e = new CacheEntry();
                    _cache[key] = e;
                }
                if (e.Pending == null)
                {
                    var task = ReadIntoAsync(key, e, read);
                    _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); // observed
                    e.Pending = task;
                }
                if (!wait && e.HasValue) return Task.FromResult((T)e.Value!);
                return (Task<T>)e.Pending;
            }
        }

        private async Task<T> ReadIntoAsync<T>(string key, CacheEntry e, Func<Task<T>> read)
        {
            await Task.Yield(); // never completes inside Cached's lock
            try
            {
                var value = await read();
                lock (_cache)
                {
                    e.Value = value;
                    e.HasValue = true;
                    e.At = DateTime.UtcNow;
                    e.Pending = null;
                    _cache[key] = e;
                }
                return value;
            }
            catch
            {
                lock (_cache)
                {
                    e.Pending = null;
                    if (!e.HasValue && _cache.TryGetValue(key, out var cur) && ReferenceEquals(cur, e)) _cache.Remove(key);
                }
                throw;
            }
        }

        private void Forget(string prefix)
        {
            lock (_cache)
            {
                foreach (var key in _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                    if (_cache[key].Pending == null) _cache.Remove(key);
            }
        }

        // =====================================================================
        // Paging and JSON helpers
        // =====================================================================

        /// <summary>Every page of a paged list endpoint (gwnAll in server.js).</summary>
        private async Task<List<JsonElement>> CallAllAsync(string path, IDictionary<string, object?> body, int pageSize = 100, int maxPages = 40)
        {
            var output = new List<JsonElement>();
            for (var page = 1; page <= maxPages; page++)
            {
                var b = new Dictionary<string, object?>(body) { ["pageNum"] = page, ["pageSize"] = pageSize };
                var d = await _client.CallAsync(path, b);
                var rows = Arr(d, "result");
                output.AddRange(rows);
                var total = Num(d, "total");
                var pages = Num(d, "totalPage") ?? Num(d, "pages");
                if (rows.Count == 0 || rows.Count < pageSize || (total != null && output.Count >= total) || (pages != null && page >= pages)) break;
            }
            return output;
        }

        private static bool Has(JsonElement o, string name, out JsonElement v)
        {
            v = default;
            return o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
        }

        private static List<JsonElement> Arr(JsonElement o, string name) =>
            Has(o, name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.Clone()).ToList() : new();

        internal static string Str(JsonElement o, string name)
        {
            if (!Has(o, name, out var v)) return "";
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => ""
            };
        }

        /// <summary>Number(v) of server.js: null for missing / "" / not a number.</summary>
        internal static double? NumOf(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            _ => null
        };

        internal static double? Num(JsonElement o, string name) => Has(o, name, out var v) ? NumOf(v) : null;

        /// <summary>JavaScript truthiness of a field (1, true, "x" = true; 0, "", null, missing = false).</summary>
        private static bool Truthy(JsonElement o, string name)
        {
            if (!Has(o, name, out var v)) return false;
            return v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.Number => v.GetDouble() != 0,
                JsonValueKind.String => (v.GetString() ?? "").Length > 0,
                JsonValueKind.Object or JsonValueKind.Array => true,
                _ => false
            };
        }

        private static string Or(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

        // =====================================================================
        // MAC, time and range helpers
        // =====================================================================

        /// <summary>12 lower-case hex digits ("" when none).</summary>
        public static string NormMac(string? mac) => Regex.Replace((mac ?? "").ToLowerInvariant(), "[^0-9a-f]", "");

        /// <summary>"AA:BB:CC:DD:EE:FF"</summary>
        public static string MacDisplay(string? mac)
        {
            var n = NormMac(mac);
            var parts = Enumerable.Range(0, n.Length / 2).Select(i => n.Substring(i * 2, 2));
            return string.Join(":", parts).ToUpperInvariant();
        }

        public static bool IsMac(string? mac) => NormMac(mac).Length == 12;

        public static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
        private static string? Iso(DateTime? utc) => utc == null ? null : Iso(utc.Value);
        private static DateTime FromMs(double ms) => DateTime.UnixEpoch.AddMilliseconds(ms);

        /// <summary>Statistics times: UTC "yyyy-MM-dd HH:mm" (or with seconds).</summary>
        public static DateTime? ParseStatTime(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return DateTime.TryParseExact(s.Trim(), new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" },
                CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : null;
        }

        /// <summary>"2026-10-23T08:35:49.974+0000" (GWN ISO with a colon-less offset).</summary>
        public static DateTime? ParseGwnIso(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var fixedText = Regex.Replace(s.Trim(), @"([+-]\d{2})(\d{2})$", "$1:$2");
            return DateTimeOffset.TryParse(fixedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.UtcDateTime : null;
        }

        public static TimeZoneInfo Zone(string? tz)
        {
            if (string.IsNullOrWhiteSpace(tz)) return TimeZoneInfo.Utc;
            try { return TimeZoneInfo.FindSystemTimeZoneById(tz); }
            catch { return TimeZoneInfo.Utc; }
        }

        /// <summary>Client first / last seen: network-local "2026/10/20 12:35PM" (or 24-hour) → UTC.</summary>
        public static DateTime? ParseLocal(string? s, string? tz)
        {
            var m = Regex.Match((s ?? "").Trim(), @"^(\d{4})/(\d{1,2})/(\d{1,2})\s+(\d{1,2}):(\d{2})\s*([AP]M)?$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            var h = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (m.Groups[6].Success) h = h % 12 + (m.Groups[6].Value.Equals("PM", StringComparison.OrdinalIgnoreCase) ? 12 : 0);
            try
            {
                var local = new DateTime(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), h, int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture), 0, DateTimeKind.Unspecified);
                var zone = Zone(tz);
                return local - zone.GetUtcOffset(local);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>Midnight today in the network's time zone, as UTC.</summary>
        public static DateTime MidnightUtc(string? tz, DateTime? nowUtc = null)
        {
            var zone = Zone(tz);
            var now = nowUtc ?? DateTime.UtcNow;
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), zone).Date;
            return DateTime.SpecifyKind(local - zone.GetUtcOffset(local), DateTimeKind.Utc);
        }

        /// <summary>Page range → GWN statistics type: 2h = 0, 1d = 1, 1w = 2, 1m = 3 (default 1d).</summary>
        public static int RangeType(string? range) => (range ?? "").Trim().ToLowerInvariant() switch
        {
            "2h" or "0" => 0,
            "1d" or "1" => 1,
            "1w" or "2" => 2,
            "1m" or "3" => 3,
            _ => 1
        };

        // =====================================================================
        // Aliases (dbo.WN_GWN_ClientAliases)
        // =====================================================================

        private async Task<T?> WithRepoAsync<T>(Func<IGwnRepository, Task<T>> action)
        {
            if (_repository != null) return await action(_repository);
            if (_scopeFactory == null) return default;
            using var scope = _scopeFactory.CreateScope();
            return await action(scope.ServiceProvider.GetRequiredService<IGwnRepository>());
        }

        private async Task ReloadAliasesAsync(bool force = false)
        {
            if (!force && DateTime.UtcNow - _aliasesAt < _coreTtl) return;
            try
            {
                var rows = await WithRepoAsync(r => r.GetAliasesAsync());
                var map = new ConcurrentDictionary<string, string>();
                foreach (var kv in rows ?? new Dictionary<string, string>())
                {
                    var mac = NormMac(kv.Key);
                    if (mac.Length == 12 && !string.IsNullOrWhiteSpace(kv.Value)) map[mac] = kv.Value;
                }
                _aliases = map;
            }
            catch (Exception ex)
            {
                if (_aliasesAt == DateTime.MinValue) _logger.LogWarning("[gwn] client aliases not read: {Error}", ex is GwnTablesMissingException ? ex.Message : ex.GetType().Name);
            }
            _aliasesAt = DateTime.UtcNow;
        }

        private GwnClientDto WithAlias(GwnClientDto c)
        {
            var copy = c.Copy();
            copy.Alias = _aliases.TryGetValue(NormMac(c.Mac), out var a) ? a : "";
            copy.Name = Or(copy.Alias, copy.GwnName, copy.Host, copy.Mac);
            return copy;
        }

        public async Task<GwnAliasResultDto?> SetAliasAsync(string? mac, string? name, string? byEmail, CancellationToken ct = default)
        {
            var n = NormMac(mac);
            if (n.Length != 12) return null;
            var alias = (name ?? "").Trim();
            if (alias.Length > 100) alias = alias[..100];
            var display = MacDisplay(n);
            if (alias.Length > 0)
            {
                await WithRepoAsync(async r => { await r.SetAliasAsync(display, alias, byEmail, ct); return true; });
                _aliases[n] = alias;
            }
            else
            {
                await WithRepoAsync(async r => { await r.DeleteAliasAsync(display, ct); return true; });
                _aliases.TryRemove(n, out _);
            }
            return new GwnAliasResultDto { Mac = display, Alias = alias };
        }

        // =====================================================================
        // Networks and locations
        // =====================================================================

        private sealed record NetRef(long Id, string Name);
        private sealed record NetDetail(string Name, string Country, string Timezone, string TimezoneDisplay);

        private Task<List<NetRef>> GwnNetworks(bool wait = false) => Cached("networks", NetworksTtl, async () =>
        {
            var d = await _client.CallAsync("v1.0.0/network/list");
            return Arr(d, "result")
                .Select(n => new NetRef((long)(Num(n, "id") ?? 0), Or(Str(n, "networkName"), $"Network {Str(n, "id")}")))
                .Where(n => n.Id > 0).ToList();
        }, wait);

        private Task<NetDetail> Detail(long id) => Cached($"detail:{id}", DetailTtl, async () =>
        {
            var d = await _client.CallAsync("v1.0.0/network/detail", new Dictionary<string, object?> { ["id"] = id });
            var country = Regex.Replace(Str(d, "countryDisplay"), @"\(.*\)$", "");
            return new NetDetail(Str(d, "networkName"), Or(country, Str(d, "country")), Or(Str(d, "timezone"), "UTC"), Str(d, "timezoneDisplay"));
        });

        private Task<NetDetail> DetailOrDefault(long id) => Detail(id).ContinueWith(t =>
            t.Status == TaskStatus.RanToCompletion ? t.Result : new NetDetail("", "", "UTC", ""), TaskScheduler.Default);

        /// <summary>(network → locations, names, available). available = false before GWN_01.</summary>
        private Task<(List<(long NetworkId, int LocationId, string? Name)> Rows, bool Available)> LocationRows(bool fresh = false) =>
            Cached("locations", fresh ? TimeSpan.Zero : LocationsTtl, async () =>
            {
                try
                {
                    var rows = await WithRepoAsync(r => r.GetNetworkLocationsAsync());
                    if (rows == null) // no database behind this service (tests)
                        return (_settings.DefaultNetworkLocations.Select(p => (p.NetworkId, p.LocationId, (string?)null)).ToList(), false);
                    return (rows.ToList(), true);
                }
                catch (Exception ex)
                {
                    // GWN_01 not run (or the database not answering): the default mapping (347087 → F-7) from settings.
                    if (Interlocked.Exchange(ref _locationsWarned, 1) == 0)
                        _logger.LogWarning("[gwn] dbo.WN_GWN_NetworkLocations not read ({Error}); using Gwn:DefaultNetworkLocations.",
                            ex is GwnTablesMissingException ? "GWN_01 not run" : ex.Message);
                    return (_settings.DefaultNetworkLocations.Select(p => (p.NetworkId, p.LocationId, (string?)null)).ToList(), false);
                }
            }, fresh);

        private int _locationsWarned;

        public async Task<IReadOnlyList<GwnNetworkDto>> GetNetworksAsync(CancellationToken ct = default)
        {
            var (rows, _) = await LocationRows();
            List<NetRef> gwn;
            try
            {
                gwn = IsConfigured ? await GwnNetworks() : new List<NetRef>();
            }
            catch (Exception ex) when (rows.Count > 0)
            {
                // GWN not answering: the networks linked to a location are still known (names from the table).
                _logger.LogWarning("[gwn] network list not read, using dbo.WN_GWN_NetworkLocations: {Error}", ex.Message);
                gwn = new List<NetRef>();
            }
            var list = gwn.Select(n => new GwnNetworkDto { Id = n.Id, Name = n.Name }).ToList();
            foreach (var row in rows.Where(r => list.All(n => n.Id != r.NetworkId)).GroupBy(r => r.NetworkId))
                list.Add(new GwnNetworkDto { Id = row.Key, Name = Or(row.Select(r => r.Name ?? "").FirstOrDefault(x => x.Length > 0) ?? "", $"Network {row.Key}") });
            foreach (var n in list)
                n.LocationIds = rows.Where(r => r.NetworkId == n.Id && r.LocationId > 0).Select(r => r.LocationId).Distinct().OrderBy(x => x).ToList();
            return list;
        }

        public async Task<GwnNetworkLocationsDto> GetNetworkLocationsAsync(bool canEdit, CancellationToken ct = default)
        {
            var (_, available) = await LocationRows();
            return new GwnNetworkLocationsDto
            {
                Available = available,
                Message = available ? null : GwnTablesMissingException.UserMessage,
                CanEdit = canEdit && available,
                Networks = (await GetNetworksAsync(ct)).ToList()
            };
        }

        public async Task<GwnNetworkDto?> SetNetworkLocationsAsync(long networkId, IReadOnlyList<int> locationIds, string? byEmail, CancellationToken ct = default)
        {
            var network = (await GwnNetworks()).FirstOrDefault(n => n.Id == networkId);
            if (network == null) return null;
            var ids = locationIds.Where(id => id > 0).Distinct().OrderBy(id => id).ToList();
            if (_repository == null && _scopeFactory == null) throw new GwnTablesMissingException();
            await WithRepoAsync(async r => { await r.SetNetworkLocationsAsync(network.Id, network.Name, ids, byEmail, ct); return true; });
            await LocationRows(fresh: true);
            return (await GetNetworksAsync(ct)).First(n => n.Id == networkId);
        }

        // =====================================================================
        // Devices, clients, SSIDs (core poll)
        // =====================================================================

        private sealed record CoreData(DateTime At, string Tz, List<GwnDeviceDto> Devices, List<GwnClientDto> Clients, List<GwnSsidDto> Ssids);

        private static bool IsRouter(string model) => Regex.IsMatch(model ?? "", @"^GWN70\d\d", RegexOptions.IgnoreCase);

        internal static GwnDeviceDto NormaliseDevice(JsonElement a)
        {
            var mac = MacDisplay(Str(a, "mac"));
            var model = Str(a, "apType");
            var ipv4 = Str(a, "ipv4");
            var ip = Str(a, "ip");
            var fw = Str(a, "versionFirmware");
            var last = Str(a, "lastFwVersion");
            var first = Num(a, "firstSeen");
            var lastSeen = Num(a, "lastSeen");
            var ch24 = Num(a, "channel");
            var ch5 = Num(a, "channel5g");
            return new GwnDeviceDto
            {
                Mac = mac,
                Name = Or(Str(a, "name"), mac),
                Model = model,
                Type = IsRouter(model) ? "Router" : "Access point",
                Status = Num(a, "status") == 1 ? "online" : "offline",
                Ip = Or(ipv4, ip),
                PublicIp = ipv4.Length > 0 && ip.Length > 0 && ip != ipv4 ? ip : "",
                Ipv6 = Str(a, "ipv6"),
                Firmware = fw,
                LatestFirmware = last.Length > 0 && last != fw ? last : "",
                UptimeSeconds = Num(a, "upTime"),
                Clients = (int)(Num(a, "clients") ?? 0),
                Download = Num(a, "download"),
                Upload = Num(a, "upload"),
                Total = Num(a, "usage"),
                Channel24 = ch24 is > 0 ? (int)ch24 : null,
                Channel5 = ch5 is > 0 ? (int)ch5 : null,
                Remarks = Str(a, "remarks"),
                FirstSeen = first is > 0 ? Iso(FromMs(first.Value)) : null,
                LastSeen = lastSeen is > 0 ? Iso(FromMs(lastSeen.Value)) : null
            };
        }

        private static string BandOf(JsonElement c)
        {
            if (Truthy(c, "wired")) return "Wired";
            var s = Str(c, "channelClassStr");
            if (s is "2.4G" or "5G" or "6G") return s;
            return Num(c, "channelClass") switch { 2 => "2.4G", 5 => "5G", 6 => "6G", _ => "" };
        }

        private static string Clean(string v) => v.Length > 0 && !Regex.IsMatch(v, "^(other|unknown|unkown)$", RegexOptions.IgnoreCase) ? v : "";

        /// <summary>"1d 16h33m" → seconds (when deltaTime is missing).</summary>
        public static double? AssocSeconds(string? s)
        {
            var m = Regex.Match(s ?? "", @"(?:(\d+)d)?\s*(?:(\d+)h)?\s*(?:(\d+)m)?");
            if (!m.Success || m.Value.Trim().Length == 0) return null;
            int G(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
            return G(1) * 86400 + G(2) * 3600 + G(3) * 60;
        }

        /// <summary>
        /// One client. GWN documents rxBytes as the client's download and txBytes as its upload (kept as is). block: 0 =
        /// blocked. firstSeen / lastSeen are network-local, else createTime / lastactive (ISO).
        /// </summary>
        internal static GwnClientDto NormaliseClient(JsonElement c, string? tz)
        {
            var mac = MacDisplay(Str(c, "clientId"));
            var gwnName = Or(Str(c, "name"), Str(c, "clientName"));
            var host = Or(Str(c, "dhcpHostname"), Str(c, "host"));
            var down = Num(c, "rxBytes") ?? 0;
            var up = Num(c, "txBytes") ?? 0;
            var apId = Str(c, "apId");
            return new GwnClientDto
            {
                Mac = mac,
                Name = Or(gwnName, host, mac),
                GwnName = gwnName,
                Ip = Str(c, "ipv4"),
                Ipv6 = Str(c, "ipv6"),
                Ssid = Str(c, "ssid"),
                SsidId = Str(c, "ssidId"),
                ApMac = apId.Length > 0 ? MacDisplay(apId) : "",
                ApName = Str(c, "apName"),
                Band = BandOf(c),
                Channel = Num(c, "channel"),
                Rssi = Num(c, "rssi"),
                ConnectedFor = Str(c, "assoctime"),
                ConnectedSeconds = Num(c, "deltaTime") ?? AssocSeconds(Str(c, "assoctime")),
                Download = down,
                Upload = up,
                Total = Num(c, "totalBytes") ?? down + up,
                Online = !Has(c, "online", out _) || Truthy(c, "online"),
                Wired = Truthy(c, "wired"),
                Guest = Truthy(c, "isGuest"),
                Blocked = Num(c, "block") == 0,
                Manufacturer = Clean(Or(Str(c, "dhcpManufacture"), Str(c, "manufacturer"))),
                Os = Clean(Or(Str(c, "dhcpOs"), Str(c, "os"))),
                Host = host,
                Vlan = Str(c, "vlanId"),
                FirstSeen = Iso(ParseLocal(Str(c, "firstSeen"), tz) ?? ParseGwnIso(Str(c, "createTime"))),
                LastSeen = Iso(ParseLocal(Str(c, "lastSeen"), tz) ?? ParseGwnIso(Str(c, "lastactive")))
            };
        }

        // Documented: 0 WEP 64, 1 WEP 128, 2 WPA/WPA2, 3 WPA2, 4 Open. 5+ are not documented (best guesses, as server.js).
        private static readonly Dictionary<int, string> Security = new()
        {
            [0] = "WEP 64-bit", [1] = "WEP 128-bit", [2] = "WPA/WPA2", [3] = "WPA2", [4] = "Open",
            [5] = "WPA3", [6] = "WPA2/WPA3", [7] = "WPA3-192", [8] = "Enhanced Open (OWE)"
        };

        internal static GwnSsidDto NormaliseSsid(JsonElement s)
        {
            var sec = Num(s, "securityMode");
            var mf = Num(s, "macFilteringEnabled");
            return new GwnSsidDto
            {
                Id = Str(s, "id"),
                Name = Or(Str(s, "ssidName"), $"SSID {Str(s, "id")}"),
                Remark = Str(s, "ssidRemark"),
                Enabled = Num(s, "wifiEnabled") == 1,
                Devices = (int)(Num(s, "onlineDevices") ?? 0),
                Security = sec == null ? "" : Security.TryGetValue((int)sec, out var label) ? label : $"Mode {sec}",
                Schedule = Num(s, "scheduleEnabled") == 1,
                Portal = Num(s, "portalEnabled") == 1,
                Isolation = Num(s, "clientIsolationEnabled") == 1,
                Vlan = Num(s, "vlanId"),
                MacFiltering = mf == 1 ? "Whitelist" : mf == 2 ? "Blacklist" : "Off"
            };
        }

        private Task<CoreData> Core(long id, bool wait = false) => Cached($"core:{id}", _coreTtl, async () =>
        {
            var det = await DetailOrDefault(id);
            var body = new Dictionary<string, object?> { ["networkId"] = id };
            var apsTask = CallAllAsync("v1.0.0/ap/list", body);
            var clientsTask = CallAllAsync("v1.0.0/client/list", body);
            var ssidsTask = CallAllAsync("v1.0.0/ssid/list", body);
            await Task.WhenAll(apsTask, clientsTask, ssidsTask);
            var devices = apsTask.Result.Select(NormaliseDevice).ToList();
            var byMac = devices.GroupBy(d => d.Mac).ToDictionary(g => g.Key, g => g.First().Name);
            var clients = clientsTask.Result.Select(c => NormaliseClient(c, det.Timezone)).ToList();
            foreach (var c in clients.Where(c => c.ApName.Length == 0))
                c.ApName = byMac.TryGetValue(c.ApMac, out var n) ? n : c.ApMac;
            return new CoreData(DateTime.UtcNow, det.Timezone, devices, clients, ssidsTask.Result.Select(NormaliseSsid).ToList());
        }, wait);

        // ap/info → { key: value }, nested key/value lists turned into objects.
        private static JsonObject InfoMap(IEnumerable<JsonElement> list)
        {
            var o = new JsonObject();
            foreach (var r in list)
            {
                var key = Str(r, "key");
                if (key.Length == 0) continue;
                if (!r.TryGetProperty("value", out var v)) { o[key] = null; continue; }
                if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0 && v[0].ValueKind == JsonValueKind.Object && v[0].TryGetProperty("key", out _))
                    o[key] = InfoMap(v.EnumerateArray());
                else
                    o[key] = JsonNode.Parse(v.GetRawText());
            }
            return o;
        }

        private Task<JsonObject> DeviceInfo(string mac, bool wait = false) => Cached($"apinfo:{NormMac(mac)}", _coreTtl, async () =>
        {
            var d = await _client.CallAsync("v1.0.0/ap/info", new Dictionary<string, object?> { ["mac"] = new[] { MacDisplay(mac) } });
            return InfoMap(Arr(d, "result"));
        }, wait);

        private static string NodeText(JsonNode? n) => n switch
        {
            null => "",
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonValue v => v.ToJsonString(),
            _ => n.ToJsonString()
        };

        private static readonly Dictionary<string, string> InfoLabels = new()
        {
            ["mac"] = "MAC address", ["apType"] = "Model", ["partNumber"] = "Part number", ["bootVersion"] = "Boot version",
            ["firmwareVersion"] = "Firmware", ["network"] = "Network", ["ip"] = "IP address", ["ipv6"] = "IPv6 address",
            ["upTime"] = "Uptime", ["ssid"] = "Wi‑Fi networks", ["loadAverage"] = "Load average (1 / 5 / 15 min)",
            ["linkSpeed"] = "Link speed", ["usedMemory"] = "Memory used", ["temperature"] = "Temperature", ["deviceName"] = "Name",
            ["deviceRemarks"] = "Remarks", ["clientBridgeMode"] = "Client bridge", ["g24"] = "2.4 GHz radio", ["g5"] = "5 GHz radio",
            ["g6"] = "6 GHz radio"
        };

        public static string FmtUptime(double sec)
        {
            var s = (long)sec;
            long d = s / 86400, h = s % 86400 / 3600, m = s % 3600 / 60;
            return d > 0 ? $"{d}d {h}h {m}m" : h > 0 ? $"{h}h {m}m" : $"{m}m";
        }

        /// <summary>ap/info as readable { key, label, value } rows (value is always text).</summary>
        internal static List<GwnInfoRowDto> InfoRows(JsonObject info)
        {
            var rows = new List<GwnInfoRowDto>();
            foreach (var (k, v) in info)
            {
                if (k is "siteId" or "longitude" or "latitude" || v == null) continue;
                if (v is JsonValue jv && (NodeText(jv) == "" || NodeText(jv) == "-1")) continue;
                if (v is JsonArray { Count: 0 }) continue;
                string value;
                if (k == "upTime" && double.TryParse(NodeText(v), NumberStyles.Float, CultureInfo.InvariantCulture, out var up)) value = FmtUptime(up);
                else if (k == "loadAverage" && v is JsonArray la)
                    value = string.Join(" / ", la.Select(x => double.TryParse(NodeText(x), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 1000
                        ? (n / 65536).ToString("0.00", CultureInfo.InvariantCulture) : NodeText(x)));
                else if (k is "g24" or "g5" or "g6" && v is JsonObject radio)
                {
                    var ch = NodeText(radio["channel"]);
                    value = $"Channel {(ch is "0" or "" ? "auto / off" : ch)} · {(radio["clients"] == null ? "0" : NodeText(radio["clients"]))} clients · power {(radio["power"] == null ? "–" : NodeText(radio["power"]))}";
                }
                else if (v is JsonArray arr)
                    value = string.Join(", ", arr.Select(x => x is JsonObject xo ? string.Join(" ", xo.Select(p => NodeText(p.Value))) : NodeText(x)));
                else if (v is JsonObject obj)
                    value = string.Join(", ", obj.Select(p => $"{p.Key}: {NodeText(p.Value)}"));
                else value = NodeText(v);
                var label = InfoLabels.TryGetValue(k, out var l) ? l
                    : Regex.Replace(Regex.Replace(k, "([A-Z])", " $1"), "^.", x => x.Value.ToUpperInvariant());
                rows.Add(new GwnInfoRowDto { Key = k, Label = label, Value = value });
            }
            return rows;
        }

        // =====================================================================
        // Statistics
        // =====================================================================

        private sealed record Series(List<DateTime?> Times, List<double?> Values);
        private sealed record Bw(List<DateTime?> Times, List<double?> DownBps, List<double?> UpBps, List<double?> Download, List<double?> Upload, double? DownloadTotal, double? UploadTotal);
        private sealed record SsidSeries(List<DateTime?> Times, List<(string Name, List<double?> Counts)> Series);

        private static List<DateTime?> Times(JsonElement d) => Arr(d, "time").Select(t => ParseStatTime(t.ValueKind == JsonValueKind.String ? t.GetString() : t.GetRawText())).ToList();

        private static List<double?> Values(JsonElement d, string name, int count)
        {
            var arr = Arr(d, name);
            return Enumerable.Range(0, count).Select(i => i < arr.Count ? NumOf(arr[i]) : null).ToList();
        }

        /// <summary>
        /// Bits per second per step → bytes per step, with the step between timestamps (5 min for 2h / 1d, 30 min for 1w,
        /// 3 h for 1m); the last step repeats the one before.
        /// </summary>
        internal static List<double?> BucketBytes(IReadOnlyList<DateTime?> times, IReadOnlyList<double?> bps)
        {
            var ms = times.Select(t => t == null ? (double?)null : (t.Value - DateTime.UnixEpoch).TotalMilliseconds).ToList();
            var output = new List<double?>();
            for (var i = 0; i < bps.Count; i++)
            {
                if (bps[i] == null || i >= ms.Count || ms[i] == null) { output.Add(null); continue; }
                var t = ms[i]!.Value;
                var prev = i > 0 && ms[i - 1] != null ? ms[i - 1]!.Value : t - 300_000;
                var next = i + 1 < ms.Count && ms[i + 1] != null ? ms[i + 1]!.Value : t + (t - prev);
                output.Add(Math.Round(bps[i]!.Value * (next - t) / 8000));
            }
            return output;
        }

        private static Bw BandwidthSeries(JsonElement d)
        {
            var times = Times(d);
            var down = Values(d, "rxBandwidth", times.Count);
            var up = Values(d, "txBandwidth", times.Count);
            return new Bw(times, down, up, BucketBytes(times, down), BucketBytes(times, up), Num(d, "rxTotal"), Num(d, "txTotal"));
        }

        private Task<Series> ClientStats(long id, int type, string mac = "", bool wait = false) =>
            Cached($"st:clients:{id}:{type}:{NormMac(mac)}", StatsTtl, async () =>
            {
                var d = mac.Length > 0
                    ? await _client.CallAsync("v1.0.0/statistics/ap/client", new Dictionary<string, object?> { ["networkId"] = id, ["type"] = type, ["mac"] = MacDisplay(mac) })
                    : await _client.CallAsync("v1.0.0/statistics/client", new Dictionary<string, object?> { ["networkId"] = id, ["type"] = type });
                var times = Times(d);
                return new Series(times, Values(d, "clientCounts", times.Count));
            }, wait);

        private Task<Bw> BandwidthStats(long id, int type, string mac = "", string client = "", bool wait = false) =>
            Cached($"st:bw:{id}:{type}:{NormMac(mac)}:{NormMac(client)}", StatsTtl, async () =>
            {
                JsonElement d;
                if (mac.Length > 0)
                    d = await _client.CallAsync("v1.0.0/statistics/ap/bandwidth", new Dictionary<string, object?> { ["networkId"] = id, ["type"] = type, ["mac"] = MacDisplay(mac) });
                else
                {
                    var body = new Dictionary<string, object?> { ["networkId"] = id, ["type"] = type };
                    if (client.Length > 0) body["clientId"] = MacDisplay(client);
                    d = await _client.CallAsync("v1.0.0/statistics/bandwidth", body);
                }
                return BandwidthSeries(d);
            }, wait);

        private Task<SsidSeries> SsidClientStats(long id, int type, string ssid = "") =>
            Cached($"st:ssid:{id}:{type}:{ssid}", StatsTtl, async () =>
            {
                var body = new Dictionary<string, object?> { ["networkId"] = id, ["type"] = type };
                if (ssid.Length > 0) body["ssidId"] = ssid;
                var d = await _client.CallAsync("v1.0.0/statistics/ssid/client", body);
                var times = Times(d);
                return new SsidSeries(times, Arr(d, "ssid").Select(s => (Str(s, "name"), Values(s, "count", times.Count))).ToList());
            });

        private sealed record Today(DateTime Since, Dictionary<string, GwnUsageDto> Devices, double Download, double Upload, double Total);

        /// <summary>Data through each device today (network time zone), from the 1-day bandwidth series.</summary>
        private async Task<Today> TodayUsage(long id, IReadOnlyList<GwnDeviceDto> devices, string tz)
        {
            var since = MidnightUtc(tz);
            var per = await Task.WhenAll(devices.Select(async d =>
            {
                try
                {
                    var s = await BandwidthStats(id, 1, d.Mac);
                    double down = 0, up = 0;
                    for (var i = 0; i < s.Times.Count; i++)
                        if (s.Times[i] >= since) { down += s.Download[i] ?? 0; up += s.Upload[i] ?? 0; }
                    return (d.Mac, Usage: new GwnUsageDto { Download = down, Upload = up, Total = down + up });
                }
                catch
                {
                    return (d.Mac, Usage: new GwnUsageDto());
                }
            }));
            var map = per.GroupBy(p => p.Mac).ToDictionary(g => g.Key, g => g.First().Usage);
            double Sum(Func<GwnUsageDto, double?> f) => per.Sum(p => f(p.Usage) ?? 0);
            return new Today(since, map, Sum(u => u.Download), Sum(u => u.Upload), Sum(u => u.Total));
        }

        // =====================================================================
        // Payloads
        // =====================================================================

        private static List<GwnAlertDto> BuildAlerts(CoreData c, IReadOnlyDictionary<string, JsonObject> infos)
        {
            var output = new List<GwnAlertDto>();
            foreach (var d in c.Devices)
            {
                if (d.Status != "online") output.Add(new GwnAlertDto { Level = "critical", Text = $"{d.Name} is offline", Where = d.Model });
                if (d.LatestFirmware.Length > 0) output.Add(new GwnAlertDto { Level = "warning", Text = $"Firmware {d.LatestFirmware} is available for {d.Name}", Where = d.Model });
                if (infos.TryGetValue(d.Mac, out var info) && TryLeadingNumber(NodeText(info["temperature"]), out var temp) && temp >= 90)
                    output.Add(new GwnAlertDto { Level = "serious", Text = $"{d.Name} is running hot ({temp.ToString(CultureInfo.InvariantCulture)} °C)", Where = d.Model });
            }
            foreach (var s in c.Ssids.Where(s => !s.Enabled))
                output.Add(new GwnAlertDto { Level = "warning", Text = $"Wi‑Fi network \"{s.Name}\" is turned off", Where = "SSID" });
            return output;
        }

        /// <summary>parseFloat of server.js ("72 °C" → 72).</summary>
        private static bool TryLeadingNumber(string s, out double value)
        {
            var m = Regex.Match(s ?? "", @"^\s*-?\d+(\.\d+)?");
            return double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && m.Success;
        }

        private async Task<(List<GwnDeviceDto> List, Dictionary<string, JsonObject> Infos, Today Today)> DevicesWithExtras(long id, CoreData c)
        {
            var infoPairs = await Task.WhenAll(c.Devices.Select(async d =>
            {
                if (d.Status != "online") return (d.Mac, Info: new JsonObject());
                try { return (d.Mac, Info: await DeviceInfo(d.Mac)); }
                catch { return (d.Mac, Info: new JsonObject()); }
            }));
            var infos = infoPairs.GroupBy(p => p.Mac).ToDictionary(g => g.Key, g => g.First().Info);
            var today = await TodayUsage(id, c.Devices, c.Tz);
            var list = c.Devices.Select(d =>
            {
                var copy = d.Copy();
                infos.TryGetValue(d.Mac, out var info);
                copy.Temperature = NodeText(info?["temperature"]);
                copy.Memory = NodeText(info?["usedMemory"]);
                copy.Today = today.Devices.TryGetValue(d.Mac, out var u) ? u : null;
                return copy;
            }).ToList();
            return (list, infos, today);
        }

        private static List<GwnSsidDto> SsidsWithClients(CoreData c) => c.Ssids.Select(s =>
        {
            var copy = s.Copy();
            copy.Clients = c.Clients.Count(x => x.SsidId == s.Id);
            return copy;
        }).ToList();

        private static GwnClientCountsDto Counts(IReadOnlyCollection<GwnClientDto> clients, IEnumerable<GwnDeviceDto> devices)
        {
            var wifi = clients.Where(x => !x.Wired).ToList();
            return new GwnClientCountsDto
            {
                Total = clients.Count,
                Band24 = wifi.Count(x => x.Band == "2.4G"),
                Band5 = wifi.Count(x => x.Band == "5G"),
                Band6 = wifi.Count(x => x.Band == "6G"),
                Wired = clients.Count - wifi.Count,
                Weak = wifi.Count(x => x.Rssi != null && x.Rssi < -75),
                DeviceReported = devices.Sum(d => d.Clients)
            };
        }

        private static GwnTrafficNowDto TrafficNow(Bw? s)
        {
            if (s == null) return new GwnTrafficNowDto();
            for (var i = s.Times.Count - 1; i >= 0; i--)
                if (s.DownBps[i] != null || s.UpBps[i] != null) return new GwnTrafficNowDto { DownloadBps = s.DownBps[i], UploadBps = s.UpBps[i] };
            return new GwnTrafficNowDto();
        }

        private async Task<Bw?> TryBandwidth(long id, int type)
        {
            try { return await BandwidthStats(id, type); }
            catch (Exception ex) { _logger.LogWarning("[gwn] bandwidth {Type} of {Id}: {Error}", type, id, ex.Message); return null; }
        }

        public async Task<GwnOverviewDto> GetOverviewAsync(GwnNetworkDto network, CancellationToken ct = default)
        {
            var detTask = DetailOrDefault(network.Id);
            var c = await Core(network.Id);
            await ReloadAliasesAsync();
            var det = await detTask;
            var (list, infos, today) = await DevicesWithExtras(network.Id, c);
            var traffic = await TryBandwidth(network.Id, 0);
            return new GwnOverviewDto
            {
                Available = true,
                UpdatedAt = Iso(c.At),
                Network = new GwnOverviewNetworkDto
                {
                    Id = network.Id,
                    Name = Or(det.Name, network.Name),
                    Country = det.Country,
                    Timezone = det.Timezone,
                    TimezoneDisplay = det.TimezoneDisplay,
                    LocationIds = network.LocationIds.ToList()
                },
                Devices = new GwnOverviewDevicesDto
                {
                    Total = c.Devices.Count,
                    Online = c.Devices.Count(d => d.Status == "online"),
                    Offline = c.Devices.Count(d => d.Status != "online"),
                    List = list
                },
                Clients = Counts(c.Clients, c.Devices),
                Usage = new GwnOverviewUsageDto { Since = Iso(today.Since), Download = today.Download, Upload = today.Upload, Total = today.Total },
                TrafficNow = TrafficNow(traffic),
                Ssids = SsidsWithClients(c),
                Alerts = BuildAlerts(c, infos)
            };
        }

        public async Task<List<GwnClientDto>> GetClientsAsync(long networkId, CancellationToken ct = default)
        {
            var c = await Core(networkId);
            await ReloadAliasesAsync();
            return c.Clients.Select(WithAlias).ToList();
        }

        private Task<List<GwnClientDto>> HistoryAll(long id, string tz) => Cached($"history:{id}", StatsTtl, async () =>
        {
            var rows = await CallAllAsync("v1.0.0/client/history/list",
                new Dictionary<string, object?> { ["networkId"] = id, ["order"] = "lastSeen", ["type"] = "desc" }, 100, 30);
            return rows.Select(r => NormaliseClient(r, tz)).ToList();
        });

        public async Task<List<GwnClientDto>> GetClientHistoryAsync(long networkId, DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            var c = await Core(networkId);
            var rows = await HistoryAll(networkId, c.Tz);
            await ReloadAliasesAsync();
            var online = new HashSet<string>(c.Clients.Select(x => x.Mac));
            DateTime? Seen(GwnClientDto r) => r.LastSeen == null ? null : DateTime.Parse(r.LastSeen, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
            return rows
                .Where(r => (from == null || (Seen(r) ?? DateTime.MinValue) >= from) && (to == null || (Seen(r) ?? DateTime.MinValue) <= to))
                .Select(r => { var x = WithAlias(r); x.Online = online.Contains(r.Mac); return x; })
                .OrderByDescending(r => Seen(r) ?? DateTime.MinValue)
                .ToList();
        }

        public async Task<GwnClientDetailDto?> GetClientAsync(long networkId, string mac, CancellationToken ct = default)
        {
            var n = NormMac(mac);
            var c = await Core(networkId);
            await ReloadAliasesAsync();
            var listed = c.Clients.FirstOrDefault(x => NormMac(x.Mac) == n);
            JsonElement? raw = null;
            string? infoError = null;
            try
            {
                raw = await Cached($"clientinfo:{networkId}:{n}", TimeSpan.FromSeconds(30), () =>
                    _client.CallAsync("v1.0.0/client/info", new Dictionary<string, object?> { ["clientId"] = MacDisplay(n), ["networkId"] = networkId }));
                if (raw.Value.ValueKind != JsonValueKind.Object) raw = null;
            }
            catch (Exception ex)
            {
                infoError = ex.Message;
            }
            if (listed == null && raw == null) return null;

            var fromInfo = raw != null ? NormaliseClient(raw.Value, c.Tz) : null;
            var baseClient = listed != null ? listed.Copy() : fromInfo!;
            if (listed != null && fromInfo != null)
            {
                // GWN's client/info fills what the list left empty; the list keeps its session figures and times.
                if (baseClient.Ip.Length == 0) baseClient.Ip = fromInfo.Ip;
                if (baseClient.Ipv6.Length == 0) baseClient.Ipv6 = fromInfo.Ipv6;
                if (baseClient.Manufacturer.Length == 0) baseClient.Manufacturer = fromInfo.Manufacturer;
                if (baseClient.Os.Length == 0) baseClient.Os = fromInfo.Os;
                if (baseClient.Host.Length == 0) baseClient.Host = fromInfo.Host;
                if (baseClient.GwnName.Length == 0) baseClient.GwnName = fromInfo.GwnName;
                if (baseClient.Vlan.Length == 0) baseClient.Vlan = fromInfo.Vlan;
                baseClient.Blocked = baseClient.Blocked || fromInfo.Blocked;
            }
            var withAlias = WithAlias(baseClient);
            var detail = new GwnClientDetailDto();
            foreach (var p in typeof(GwnClientDto).GetProperties().Where(p => p.CanWrite))
                p.SetValue(detail, p.GetValue(withAlias));
            detail.Online = listed != null;
            detail.LastActive = raw != null ? Iso(ParseStatTime(Str(raw.Value, "lastactiveTime")) ?? ParseGwnIso(Str(raw.Value, "lastactiveTime"))) : null;
            detail.Raw = raw ?? JsonDocument.Parse("{}").RootElement.Clone();
            detail.InfoError = infoError;
            return detail;
        }

        private static GwnBandwidthDto ToDto(Bw s) => new()
        {
            Times = s.Times.Select(Iso).ToList(),
            Download = s.Download,
            Upload = s.Upload,
            DownloadBps = s.DownBps,
            UploadBps = s.UpBps,
            DownloadTotal = s.DownloadTotal,
            UploadTotal = s.UploadTotal
        };

        public async Task<GwnBandwidthDto> GetClientUsageAsync(long networkId, string mac, string? range, CancellationToken ct = default) =>
            ToDto(await BandwidthStats(networkId, RangeType(range), client: NormMac(mac)));

        public async Task<List<GwnDeviceDto>> GetDevicesAsync(long networkId, CancellationToken ct = default)
        {
            var c = await Core(networkId);
            return (await DevicesWithExtras(networkId, c)).List;
        }

        public async Task<GwnDeviceDetailDto?> GetDeviceAsync(long networkId, string mac, CancellationToken ct = default)
        {
            var n = NormMac(mac);
            var c = await Core(networkId);
            var device = c.Devices.FirstOrDefault(d => NormMac(d.Mac) == n);
            if (device == null) return null;
            await ReloadAliasesAsync();
            JsonObject info = new();
            string? infoError = null;
            try { info = await DeviceInfo(n); }
            catch (Exception ex) { infoError = ex.Message; }
            var today = await TodayUsage(networkId, new[] { device }, c.Tz);

            var detail = new GwnDeviceDetailDto();
            foreach (var p in typeof(GwnDeviceDto).GetProperties().Where(p => p.CanWrite))
                p.SetValue(detail, p.GetValue(device));
            detail.Temperature = NodeText(info["temperature"]);
            detail.Memory = NodeText(info["usedMemory"]);
            detail.Today = today.Devices.TryGetValue(device.Mac, out var u) ? u : null;
            detail.Info = InfoRows(info);
            detail.InfoError = infoError;
            detail.Radios = new[] { ("g24", "2.4 GHz"), ("g5", "5 GHz"), ("g6", "6 GHz") }
                .Where(r => info[r.Item1] is JsonObject)
                .Select(r =>
                {
                    var o = (JsonObject)info[r.Item1]!;
                    double? N(string k) => double.TryParse(NodeText(o[k]), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : null;
                    return new GwnRadioDto { Band = r.Item2, Channel = N("channel"), Clients = N("clients"), Power = NodeText(o["power"]) };
                }).ToList();
            detail.ClientList = c.Clients.Where(x => NormMac(x.ApMac) == n).Select(WithAlias).ToList();
            return detail;
        }

        /// <summary>Several series on one sorted time axis (missing = null).</summary>
        private static (List<DateTime> Times, List<List<double?>> Values) Merge(params (List<DateTime?> Times, List<double?> Values)[] parts)
        {
            var all = parts.SelectMany(p => p.Times).Where(t => t != null).Select(t => t!.Value).Distinct().OrderBy(t => t).ToList();
            var values = parts.Select(p =>
            {
                var map = new Dictionary<DateTime, double?>();
                for (var i = 0; i < p.Times.Count; i++)
                    if (p.Times[i] != null) map[p.Times[i]!.Value] = i < p.Values.Count ? p.Values[i] : null;
                return all.Select(t => map.TryGetValue(t, out var v) ? v : null).ToList();
            }).ToList();
            return (all, values);
        }

        public async Task<GwnDeviceStatsDto> GetDeviceStatsAsync(long networkId, string mac, string? range, CancellationToken ct = default)
        {
            var type = RangeType(range);
            var n = NormMac(mac);
            var clTask = ClientStats(networkId, type, n);
            var bwTask = BandwidthStats(networkId, type, n);
            await Task.WhenAll(clTask, bwTask);
            var cl = clTask.Result;
            var bw = bwTask.Result;
            var m = Merge((cl.Times, cl.Values), (bw.Times, bw.Download), (bw.Times, bw.Upload), (bw.Times, bw.DownBps), (bw.Times, bw.UpBps));
            return new GwnDeviceStatsDto
            {
                Times = m.Times.Select(t => (string?)Iso(t)).ToList(),
                Clients = m.Values[0],
                Download = m.Values[1],
                Upload = m.Values[2],
                DownloadBps = m.Values[3],
                UploadBps = m.Values[4],
                DownloadTotal = bw.DownloadTotal,
                UploadTotal = bw.UploadTotal
            };
        }

        private static readonly Dictionary<string, string> BandNames = new() { ["2"] = "2.4 GHz", ["5"] = "5 GHz", ["6"] = "6 GHz" };

        // Only non-secret settings leave the server (never keys, passphrases or RADIUS secrets).
        private Task<GwnSsidConfigDto> SsidConfig(string id) => Cached($"ssidcfg:{id}", NetworksTtl, async () =>
        {
            var d = await _client.CallAsync("v1.0.0/ssid/configuration",
                new Dictionary<string, object?> { ["id"] = long.TryParse(id, out var n) ? n : id });
            var c = Has(d, "configuration", out var cfg) ? cfg : default;
            var bands = Or(Str(c, "ssidNewSsidBand"), Str(c, "ssidSsidBand"));
            return new GwnSsidConfigDto
            {
                Bands = bands.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(b => BandNames.TryGetValue(b, out var x) ? x : b).ToList(),
                Hidden = Truthy(c, "ssidSsidHidden"),
                ClientLimit = Num(c, "ssidWifiClientLimit"),
                VlanId = Truthy(c, "ssidVlan") ? Num(c, "ssidVlanid") : null,
                Roaming80211r = Truthy(c, "ssid11R"),
                Roaming80211k = Truthy(c, "ssid11K"),
                Roaming80211v = Truthy(c, "ssid11V"),
                IdleTimeout = Num(c, "ssidStaIdleTimeout"),
                Mlo = Truthy(c, "ssidEnableMlo")
            };
        });

        public async Task<List<GwnSsidDto>> GetSsidsAsync(long networkId, CancellationToken ct = default)
        {
            var c = await Core(networkId);
            return (await Task.WhenAll(SsidsWithClients(c).Select(async s =>
            {
                GwnSsidConfigDto? config = null;
                try { config = await SsidConfig(s.Id); } catch { /* shown without its settings */ }
                var cl = c.Clients.Where(x => x.SsidId == s.Id).ToList();
                s.Vlan ??= config?.VlanId;
                s.Config = config;
                s.Bands = cl.GroupBy(x => x.Band.Length > 0 ? x.Band : "Other").ToDictionary(g => g.Key, g => g.Count());
                s.Weak = cl.Count(x => x.Rssi != null && x.Rssi < -75);
                s.Download = cl.Sum(x => x.Download);
                s.Upload = cl.Sum(x => x.Upload);
                return s;
            }))).ToList();
        }

        public async Task<GwnCountSeriesDto> GetClientStatsAsync(long networkId, string? range, CancellationToken ct = default)
        {
            var s = await ClientStats(networkId, RangeType(range));
            return new GwnCountSeriesDto { Times = s.Times.Select(Iso).ToList(), Counts = s.Values };
        }

        public async Task<GwnBandwidthDto> GetBandwidthStatsAsync(long networkId, string? range, CancellationToken ct = default) =>
            ToDto(await BandwidthStats(networkId, RangeType(range)));

        public async Task<GwnSsidStatsDto> GetSsidStatsAsync(long networkId, string? range, string? ssidId, CancellationToken ct = default)
        {
            var type = RangeType(range);
            var ssid = (ssidId ?? "").Trim();
            if (ssid.Length > 0)
            {
                // Only that SSID's own series; zeros when it has none. GWN rejects unknown SSID ids ("Permission Denied"),
                // so those are answered from the all-SSID times without asking.
                var name = (await Core(networkId)).Ssids.FirstOrDefault(x => x.Id == ssid)?.Name;
                var one = name != null ? await SsidClientStats(networkId, type, ssid) : await SsidClientStats(networkId, type);
                var own = name != null ? one.Series.FirstOrDefault(x => x.Name == name) : default;
                return new GwnSsidStatsDto
                {
                    Times = one.Times.Select(Iso).ToList(),
                    Counts = own.Counts ?? one.Times.Select(_ => (double?)0).ToList(),
                    Series = own.Counts != null ? new List<GwnSsidSeriesDto> { new() { Name = own.Name, Counts = own.Counts } } : new()
                };
            }
            var s = await SsidClientStats(networkId, type);
            var counts = s.Times.Select((_, i) =>
            {
                double? sum = null;
                foreach (var x in s.Series)
                    if (i < x.Counts.Count && x.Counts[i] != null) sum = (sum ?? 0) + x.Counts[i];
                return sum;
            }).ToList();
            return new GwnSsidStatsDto
            {
                Times = s.Times.Select(Iso).ToList(),
                Counts = counts,
                Series = s.Series.Select(x => new GwnSsidSeriesDto { Name = x.Name, Counts = x.Counts }).ToList()
            };
        }

        // =====================================================================
        // Actions (super admin; dbo.WN_GWN_ActionLog)
        // =====================================================================

        private async Task LogActionAsync(GwnActionLogEntry entry)
        {
            try
            {
                await WithRepoAsync(async r => { await r.AddActionAsync(entry); return true; });
            }
            catch (Exception ex)
            {
                // The change on GWN stands either way; the API log keeps the line.
                _logger.LogError("[gwn] action log not written ({Action} {Target} by {By}, ok={Ok}): {Error}",
                    entry.Action, entry.Target, entry.ByEmail, entry.Ok, ex.Message);
            }
        }

        public async Task<GwnBlockResultDto> BlockClientAsync(long networkId, string mac, bool block, string? byEmail, int? byUserId, CancellationToken ct = default)
        {
            var display = MacDisplay(mac);
            var action = block ? "block" : "unblock";
            try
            {
                // GWN client/block: block 0 = block, 1 = unblock; clientId = the client's MAC.
                await _client.CallAsync("v1.0.0/client/block",
                    new Dictionary<string, object?> { ["block"] = block ? 0 : 1, ["clientId"] = display, ["networkId"] = networkId }, ct);
            }
            catch (Exception ex)
            {
                await LogActionAsync(new GwnActionLogEntry(networkId, action, display, byEmail, byUserId, false, Trim500(ex.Message)));
                throw;
            }
            await LogActionAsync(new GwnActionLogEntry(networkId, action, display, byEmail, byUserId, true, null));
            Forget($"core:{networkId}");
            Forget($"clientinfo:{networkId}:");
            _logger.LogInformation("[gwn] {Action} {Mac} on network {Network} by {By}", action, display, networkId, byEmail);
            return new GwnBlockResultDto { Mac = display, Blocked = block };
        }

        public async Task<GwnRebootResultDto?> RebootDeviceAsync(long networkId, string mac, string? byEmail, int? byUserId, CancellationToken ct = default)
        {
            var n = NormMac(mac);
            var c = await Core(networkId);
            var device = c.Devices.FirstOrDefault(d => NormMac(d.Mac) == n);
            if (device == null) return null;
            try
            {
                // GWN ap/reboot: { "mac": ["000b82aaaaaa"] } (a list; the spec's own format).
                await _client.CallAsync("v1.0.0/ap/reboot", new Dictionary<string, object?> { ["mac"] = new[] { n } }, ct);
            }
            catch (Exception ex)
            {
                await LogActionAsync(new GwnActionLogEntry(networkId, "reboot", device.Mac, byEmail, byUserId, false, Trim500(ex.Message)));
                throw;
            }
            await LogActionAsync(new GwnActionLogEntry(networkId, "reboot", device.Mac, byEmail, byUserId, true, device.Name));
            Forget($"core:{networkId}");
            Forget($"apinfo:{n}");
            _logger.LogInformation("[gwn] reboot {Mac} ({Name}) on network {Network} by {By}", device.Mac, device.Name, networkId, byEmail);
            return new GwnRebootResultDto { Mac = device.Mac, Rebooting = true };
        }

        private static string Trim500(string s) => s.Length > 500 ? s[..500] : s;

        public async Task<List<GwnActionLogDto>> GetActionsAsync(long? networkId, int top, CancellationToken ct = default)
        {
            try
            {
                return await WithRepoAsync(r => r.GetActionsAsync(networkId, Math.Clamp(top, 1, 500), ct)) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[gwn] action log not read: {Error}", ex is GwnTablesMissingException ? "GWN_01 not run" : ex.Message);
                return new();
            }
        }

        // =====================================================================
        // Polling
        // =====================================================================

        public async Task RefreshCoreAsync(CancellationToken ct = default)
        {
            if (UnavailableMessage != null) return;
            List<NetRef> nets;
            try
            {
                nets = await GwnNetworks();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError("[gwn core] {Error}", ex.Message);
                return;
            }
            await ReloadAliasesAsync(force: true);
            foreach (var net in nets)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var c = await Core(net.Id, wait: true);
                    // Device details (temperature, memory) for the overview / devices pages.
                    await Task.WhenAll(c.Devices.Where(d => d.Status == "online").Select(d => RefreshDeviceInfo(d.Mac)));
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogError("[gwn core] network {Id}: {Error}", net.Id, ex.Message);
                }
            }
        }

        private async Task RefreshDeviceInfo(string mac)
        {
            try
            {
                await DeviceInfo(mac, wait: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[gwn core] device info {Mac}: {Error}", mac, ex.Message);
            }
        }

        public async Task RefreshStatsAsync(CancellationToken ct = default)
        {
            if (UnavailableMessage != null) return;
            List<NetRef> nets;
            try { nets = await GwnNetworks(); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { _logger.LogError("[gwn stats] {Error}", ex.Message); return; }
            foreach (var net in nets)
            {
                ct.ThrowIfCancellationRequested();
                var tasks = new List<Task>
                {
                    ClientStats(net.Id, 0, wait: true), ClientStats(net.Id, 1, wait: true),
                    BandwidthStats(net.Id, 0, wait: true), BandwidthStats(net.Id, 1, wait: true)
                };
                try
                {
                    var c = await Core(net.Id);
                    tasks.AddRange(c.Devices.Select(d => (Task)BandwidthStats(net.Id, 1, d.Mac, wait: true)));
                }
                catch { /* core errors are logged by RefreshCoreAsync */ }
                try { await Task.WhenAll(tasks); }
                catch (Exception ex) when (!ct.IsCancellationRequested) { _logger.LogError("[gwn stats] network {Id}: {Error}", net.Id, ex.Message); }
            }
        }

        // =====================================================================
        // CEO / Sales Manager dashboards
        // =====================================================================

        public async Task<GwnDashboardSectionDto> GetDashboardSectionAsync(IReadOnlyList<GwnNetworkDto> networks, CancellationToken ct = default)
        {
            var section = new GwnDashboardSectionDto
            {
                Networks = networks.Select(n => new GwnDashboardNetworkDto { Id = n.Id, Name = n.Name, LocationIds = n.LocationIds.ToList() }).ToList()
            };
            if (UnavailableMessage != null)
            {
                section.Available = false;
                section.Message = UnavailableMessage;
                return section;
            }
            try
            {
                var build = BuildDashboardAsync(section, networks);
                var done = await Task.WhenAny(build, Task.Delay(DashboardWait, ct));
                if (done != build)
                {
                    ct.ThrowIfCancellationRequested();
                    return new GwnDashboardSectionDto { Available = false, Message = NoDataYetMessage, Networks = section.Networks };
                }
                return await build;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[gwn] dashboard section failed: {Error}", ex.Message);
                return new GwnDashboardSectionDto { Available = false, Message = ReadFailedMessage, Networks = section.Networks };
            }
        }

        private async Task<GwnDashboardSectionDto> BuildDashboardAsync(GwnDashboardSectionDto section, IReadOnlyList<GwnNetworkDto> networks)
        {
            await ReloadAliasesAsync();
            var cores = new List<(GwnNetworkDto Net, CoreData Core)>();
            foreach (var n in networks) cores.Add((n, await Core(n.Id)));

            var clients = cores.SelectMany(x => x.Core.Clients).ToList();
            var devices = cores.SelectMany(x => x.Core.Devices).ToList();
            var counts = Counts(clients, devices);
            section.UpdatedAt = Iso(cores.Count == 0 ? DateTime.UtcNow : cores.Min(x => x.Core.At));
            section.Clients = new GwnDashboardClientsDto
            {
                Total = counts.Total, Band24 = counts.Band24, Band5 = counts.Band5, Wired = counts.Wired, Weak = counts.Weak, DeviceReported = counts.DeviceReported
            };

            var deviceRows = new List<GwnDashboardDeviceDto>();
            double down = 0, up = 0;
            double? nowDown = null, nowUp = null;
            var historyParts = new List<(List<DateTime?> Times, List<double?> Values)>();
            var downParts = new List<(List<DateTime?> Times, List<double?> Values)>();
            var upParts = new List<(List<DateTime?> Times, List<double?> Values)>();
            var alerts = new List<GwnDashboardAlertDto>();
            foreach (var (net, core) in cores)
            {
                var (list, infos, today) = await DevicesWithExtras(net.Id, core);
                deviceRows.AddRange(list.Select(d => new GwnDashboardDeviceDto
                {
                    Mac = d.Mac, Name = d.Name, Model = d.Model, Status = d.Status, Clients = d.Clients, Temperature = d.Temperature
                }));
                down += today.Download;
                up += today.Upload;
                alerts.AddRange(BuildAlerts(core, infos).Where(a => a.Level is "critical" or "serious")
                    .Select(a => new GwnDashboardAlertDto { Level = a.Level, Text = networks.Count > 1 ? $"{net.Name}: {a.Text}" : a.Text }));

                var now = TrafficNow(await TryBandwidth(net.Id, 0));
                if (now.DownloadBps != null) nowDown = (nowDown ?? 0) + now.DownloadBps;
                if (now.UploadBps != null) nowUp = (nowUp ?? 0) + now.UploadBps;

                try
                {
                    var h = await ClientStats(net.Id, 1);
                    historyParts.Add((h.Times, h.Values));
                }
                catch (Exception ex) { _logger.LogWarning("[gwn] client history of {Id}: {Error}", net.Id, ex.Message); }
                var bw = await TryBandwidth(net.Id, 1);
                if (bw != null)
                {
                    downParts.Add((bw.Times, bw.DownBps));
                    upParts.Add((bw.Times, bw.UpBps));
                }
            }

            section.Devices = new GwnDashboardDevicesDto
            {
                Total = devices.Count,
                Online = devices.Count(d => d.Status == "online"),
                Offline = devices.Count(d => d.Status != "online"),
                List = deviceRows
            };
            section.UsageToday = new GwnDashboardUsageDto { Download = down, Upload = up, Total = down + up };
            section.TrafficNow = new GwnDashboardTrafficDto { DownloadBps = nowDown, UploadBps = nowUp };

            var history = SumSeries(historyParts);
            section.ClientHistory = new GwnDashboardCountSeriesDto { Times = history.Times, Counts = history.Values };
            var dl = SumSeries(downParts);
            var ul = SumSeries(upParts);
            section.Bandwidth = new GwnDashboardBandwidthDto { Times = dl.Times, DownloadBps = dl.Values, UploadBps = ul.Values };

            section.TopClients = clients.Select(WithAlias).OrderByDescending(c => c.Total).Take(5)
                .Select(c => new GwnDashboardTopClientDto { Mac = c.Mac, Name = c.Name, Download = c.Download, Upload = c.Upload, Total = c.Total }).ToList();
            section.Alerts = alerts;
            section.Available = true;
            return section;
        }

        /// <summary>Several networks' series added up on one time axis (a step with no value anywhere stays null).</summary>
        private static (List<string?> Times, List<double?> Values) SumSeries(List<(List<DateTime?> Times, List<double?> Values)> parts)
        {
            if (parts.Count == 0) return (new(), new());
            var m = Merge(parts.ToArray());
            var values = m.Times.Select((_, i) =>
            {
                double? sum = null;
                foreach (var v in m.Values) if (v[i] != null) sum = (sum ?? 0) + v[i];
                return sum;
            }).ToList();
            return (m.Times.Select(t => (string?)Iso(t)).ToList(), values);
        }
    }
}
