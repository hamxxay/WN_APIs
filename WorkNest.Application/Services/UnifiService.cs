using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// UniFi network dashboard — port of "Unifi UI" server.js. Singleton: holds the polled cloud state
    /// (hosts, sites, devices, ISP metrics), a rolling client-count history and client aliases, plus a
    /// stale-while-revalidate cache for console Network API calls.
    /// Payloads are built as JsonObject so field names (and map keys such as "WAN1" or site ids) are written
    /// exactly as the Node server wrote them, regardless of the API's camelCase naming policy.
    /// History and aliases are persisted in WN_UNIFI_History / WN_UNIFI_ClientAliases; if those tables are
    /// unavailable the dashboard keeps working in memory.
    /// </summary>
    public class UnifiService : IUnifiService
    {
        private const string DefaultPlaceholderNames = @"not\s*installed|placeholder|\bspare\b|^-?\s*free\b";
        private const long DayMs = 86_400_000;
        private const long HourMs = 3_600_000;

        private readonly IUnifiClient _client;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UnifiService> _logger;
        private readonly string _brandName;
        private readonly int _historyDays;
        private readonly Regex _placeholderRe;

        // ---- Polled state (lists are replaced, never mutated, so readers need no lock) ----
        private sealed record DeviceRow(JsonElement D, string? HostId, string? HostName);
        private sealed record HistorySample(DateTime T, int Wifi, int Wired, int Guest, int Online, int Offline, IReadOnlyList<KeyValuePair<string, double[]>> Sites);

        private volatile string? _updatedAt;
        private volatile string? _ispUpdatedAt;
        private volatile string? _error;
        private List<JsonElement> _hosts = new();
        private List<JsonElement> _sites = new();
        private List<DeviceRow> _devices = new();
        private List<JsonElement> _isp = new();
        private readonly List<HistorySample> _history = new();
        private readonly object _historyLock = new();
        private ConcurrentDictionary<string, string> _aliases = new();
        private readonly TaskCompletionSource _coreReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Task? _initTask;
        private readonly object _initLock = new();
        private int _dbWarned;
        private DateTime _lastHistoryCleanup = DateTime.MinValue;

        public UnifiService(IUnifiClient client, IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<UnifiService> logger)
        {
            _client = client;
            _scopeFactory = scopeFactory;
            _logger = logger;
            RefreshSeconds = Math.Max(30, int.TryParse(configuration["Unifi:RefreshSeconds"], out var rs) ? rs : 60);
            _historyDays = int.TryParse(configuration["Unifi:HistoryDays"], out var hd) && hd > 0 ? hd : 7;
            _brandName = configuration["Unifi:BrandName"] ?? string.Empty;
            var placeholder = configuration["Unifi:PlaceholderNames"];
            _placeholderRe = new Regex(string.IsNullOrWhiteSpace(placeholder) ? DefaultPlaceholderNames : placeholder, RegexOptions.IgnoreCase);
            if (!_client.IsConfigured) _error = "Unifi:ApiKey is not set in appsettings";
        }

        public bool IsConfigured => _client.IsConfigured;
        public int RefreshSeconds { get; }

        // =====================================================================
        // Polling
        // =====================================================================

        public Task InitializeAsync(CancellationToken ct = default)
        {
            lock (_initLock)
            {
                return _initTask ??= LoadFromDbAsync();
            }
        }

        private async Task LoadFromDbAsync()
        {
            await WithDbAsync(async db =>
            {
                var rows = await db.GetUnifiHistoryDbAsync(_historyDays);
                var samples = rows.Select(r => new HistorySample(r.SampledAt, r.Wifi, r.Wired, r.Guest, r.Online, r.Offline, ParseSites(r.SitesJson))).ToList();
                lock (_historyLock)
                {
                    // Keep anything recorded in memory before the load finished.
                    var newer = _history.Where(h => samples.Count == 0 || h.T > samples[^1].T).ToList();
                    _history.Clear();
                    _history.AddRange(samples);
                    _history.AddRange(newer);
                }
            });
            await ReloadAliasesAsync();
        }

        private async Task ReloadAliasesAsync()
        {
            await WithDbAsync(async db =>
            {
                var rows = await db.GetUnifiClientAliasesDbAsync();
                var map = new ConcurrentDictionary<string, string>();
                foreach (var kv in rows)
                {
                    var mac = NormMac(kv.Key);
                    if (mac.Length == 12 && !string.IsNullOrEmpty(kv.Value)) map[mac] = kv.Value;
                }
                _aliases = map;
            });
        }

        public async Task RefreshCoreAsync(CancellationToken ct = default)
        {
            try
            {
                await PollCoreAsync(ct);
                _updatedAt = Iso(DateTime.UtcNow);
                _error = null;
                _coreReady.TrySetResult();
                await RecordHistoryAsync();
                // Pick up names saved by other API instances.
                await ReloadAliasesAsync();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _error = ex.Message;
                _logger.LogError("[unifi core] {Error}", ex.Message);
            }
        }

        public async Task RefreshIspAsync(CancellationToken ct = default)
        {
            try
            {
                var json = await _client.GetAsync("/ea/isp-metrics/5m", new Dictionary<string, string> { ["duration"] = "24h" }, ct);
                _isp = Arr(P(json, "data")).ToList();
                _ispUpdatedAt = Iso(DateTime.UtcNow);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError("[unifi isp] {Error}", ex.Message);
                if (_error == null) _error = ex.Message;
            }
        }

        public async Task WarmLogsAsync()
        {
            try { await NetLogs(); }
            catch (Exception ex) { _logger.LogError("[unifi logs] {Error}", ex.Message); }
        }

        private async Task PollCoreAsync(CancellationToken ct)
        {
            var hostsTask = _client.GetAllAsync("/v1/hosts", null, ct);
            var sitesTask = _client.GetAllAsync("/v1/sites", null, ct);
            var devicesTask = _client.GetAllAsync("/v1/devices", null, ct);
            await Task.WhenAll(hostsTask, sitesTask, devicesTask);

            _hosts = hostsTask.Result;
            _sites = sitesTask.Result;
            _devices = devicesTask.Result
                .SelectMany(g => Arr(P(g, "devices")).Select(d => new DeviceRow(d, Str(P(g, "hostId")), Str(P(g, "hostName")))))
                .ToList();
        }

        // =====================================================================
        // Normalisation (cloud API)
        // =====================================================================

        private bool IsPlaceholder(JsonElement? name) => _placeholderRe.IsMatch(Truthy(name) ? Str(name) ?? "" : "");

        private JsonElement? HostOf(string? hostId) => Find(_hosts, h => Str(P(h, "id")) == hostId);

        private string HostName(string? hostId)
        {
            var h = HostOf(hostId);
            return S(P(h, "reportedState", "name"), P(h, "reportedState", "hostname"), P(h, "userData", "name"));
        }

        private string SiteName(JsonElement s)
        {
            var n = S(P(s, "meta", "desc"), P(s, "meta", "name"));
            if (n == "") n = "Site";
            if (n == "Default" || n == "default")
            {
                var host = HostName(Str(P(s, "hostId")));
                return host != "" ? host : n;
            }
            return n;
        }

        // "WAN" → "WAN1", "WAN2" stays "WAN2".
        private static string WanLabel(string key) => Regex.IsMatch(key, "^WAN$", RegexOptions.IgnoreCase) ? "WAN1" : key.ToUpperInvariant();

        private List<JsonObject> NormaliseWans(JsonElement s)
        {
            var ports = Arr(P(HostOf(Str(P(s, "hostId"))), "reportedState", "wans")).ToList();
            var wans = new List<(JsonObject Obj, double Priority, string Label)>();
            if (P(s, "statistics", "wans") is { ValueKind: JsonValueKind.Object } map)
            {
                foreach (var entry in map.EnumerateObject())
                {
                    var key = entry.Name;
                    var w = entry.Value;
                    var port = Find(ports, p => Str(P(p, "type")) == key);
                    var label = WanLabel(key);
                    wans.Add((new JsonObject
                    {
                        ["key"] = key,
                        ["label"] = label,
                        ["isp"] = S(P(w, "ispInfo", "name")),
                        ["organization"] = S(P(w, "ispInfo", "organization")),
                        ["externalIp"] = S(P(w, "externalIp")),
                        ["up"] = !IsFalse(P(w, "portUp")) && !IsFalse(P(port, "plugged")),
                        ["uptime"] = R(P(w, "wanUptime")),
                        ["priority"] = R(P(w, "failoverPriority")),
                        ["issues"] = Arr(P(w, "wanIssues")).Count(),
                        ["port"] = R(P(port, "port")),
                        ["speed"] = S(P(port, "speedType")),
                        ["enabled"] = !IsFalse(P(port, "enabled")),
                    }, D(P(w, "failoverPriority")) ?? 99, label));
                }
            }
            return wans.OrderBy(x => x.Priority).ThenBy(x => x.Label, LocaleCompare).Select(x => x.Obj).ToList();
        }

        private List<JsonObject> NormaliseSites()
        {
            return _sites.Select(s =>
            {
                var st = P(s, "statistics");
                var c = P(st, "counts");
                var host = P(HostOf(Str(P(s, "hostId"))), "reportedState");
                var wans = NormaliseWans(s);
                var isp = S(P(st, "ispInfo", "name"));
                if (isp == "" && wans.Count > 0) isp = ToStr(wans[0]["isp"]);
                return new JsonObject
                {
                    ["id"] = R(P(s, "siteId")),
                    ["hostId"] = R(P(s, "hostId")),
                    ["name"] = SiteName(s),
                    ["console"] = HostName(Str(P(s, "hostId"))),
                    ["consoleVersion"] = S(P(host, "version")),
                    ["wifiClients"] = N(Num(P(c, "wifiClient"))),
                    ["wiredClients"] = N(Num(P(c, "wiredClient"))),
                    ["guestClients"] = N(Num(P(c, "guestClient"))),
                    ["totalDevices"] = N(Num(P(c, "totalDevice"))),
                    ["offlineDevices"] = N(Num(P(c, "offlineDevice"))),
                    ["pendingUpdates"] = N(Num(P(c, "pendingUpdateDevice"))),
                    ["criticalNotifications"] = N(Num(P(c, "criticalNotification"))),
                    ["wanUptime"] = R(P(st, "percentages", "wanUptime")),
                    ["txRetry"] = R(P(st, "percentages", "txRetry")),
                    ["isp"] = isp,
                    ["gateway"] = S(P(st, "gateway", "shortname")),
                    ["ipsMode"] = S(P(st, "gateway", "ipsMode")),
                    ["loadBalancing"] = S(P(st, "wanLoadBalancingMode")),
                    ["wans"] = new JsonArray(wans.ToArray<JsonNode?>()),
                };
            }).ToList();
        }

        private List<JsonObject> NormaliseDevices()
        {
            return _devices.Select(row =>
            {
                var d = row.D;
                var placeholder = IsPlaceholder(P(d, "name"));
                var rawStatus = S(P(d, "status")).ToLowerInvariant();
                var host = row.HostName;
                return new JsonObject
                {
                    ["id"] = R(P(d, "id")),
                    ["name"] = S(P(d, "name"), P(d, "model"), P(d, "mac")),
                    ["model"] = S(P(d, "model"), P(d, "shortname")),
                    ["productLine"] = S(P(d, "productLine")),
                    ["type"] = DeviceType(d),
                    ["ip"] = S(P(d, "ip")),
                    ["mac"] = S(P(d, "mac")),
                    ["status"] = placeholder && rawStatus != "online" ? "not installed" : (rawStatus == "" ? "unknown" : rawStatus),
                    ["placeholder"] = placeholder,
                    ["version"] = S(P(d, "version")),
                    ["updateAvailable"] = Truthy(P(d, "updateAvailable")) ? R(P(d, "updateAvailable")) : null,
                    ["hostId"] = row.HostId,
                    ["host"] = string.IsNullOrEmpty(host) ? HostName(row.HostId) : host,
                    ["isConsole"] = Truthy(P(d, "isConsole")),
                    ["startupTime"] = Truthy(P(d, "startupTime")) ? R(P(d, "startupTime")) : null,
                };
            }).ToList();
        }

        private static readonly Regex SwitchRe = new(@"\busw|switch|\bus-|flex mini|\busl");
        private static readonly Regex GatewayRe = new(@"\budm|\bucg|\buxg|\busg|\budr|\buck|gateway|express");
        private static readonly Regex ApRe = new(@"\bu[5-7]|\buap|\bual|nanohd|flexhd|beacon|mesh|\bap\b|\be7\b");

        private static string DeviceType(JsonElement d)
        {
            if (Truthy(P(d, "isConsole"))) return "Console / Gateway";
            var m = $"{S(P(d, "model"))} {S(P(d, "shortname"))} {S(P(d, "name"))}".ToLowerInvariant();
            var productLine = Str(P(d, "productLine"));
            if (productLine == "protect") return "Camera / Protect";
            if (productLine == "access") return "Access";
            if (productLine == "talk") return "Phone";
            if (SwitchRe.IsMatch(m)) return "Switch";
            if (GatewayRe.IsMatch(m)) return "Console / Gateway";
            if (ApRe.IsMatch(m)) return "Access point";
            return "Other";
        }

        private List<JsonObject> NormaliseIsp()
        {
            var sites = _sites;
            return _isp.Select(entry =>
            {
                var siteId = Str(P(entry, "siteId"));
                var site = Find(sites, s => Str(P(s, "siteId")) == siteId);
                string name;
                if (site is { } found) name = SiteName(found);
                else
                {
                    name = HostName(Str(P(entry, "hostId")));
                    if (name == "") name = siteId ?? "";
                }
                var points = Arr(P(entry, "periods"))
                    .Select(p =>
                    {
                        var w = P(p, "data", "wan");
                        var down = D(P(w, "download_kbps"));
                        var up = D(P(w, "upload_kbps"));
                        return (Time: ParseDate(Str(P(p, "metricTime"))), Obj: new JsonObject
                        {
                            ["t"] = R(P(p, "metricTime")),
                            ["latency"] = R(P(w, "avgLatency")),
                            ["maxLatency"] = R(P(w, "maxLatency")),
                            ["down"] = down != null ? N(down / 1000) : null,
                            ["up"] = up != null ? N(up / 1000) : null,
                            ["loss"] = R(P(w, "packetLoss")),
                            ["uptime"] = R(P(w, "uptime")),
                            ["isp"] = S(P(w, "ispName")),
                        });
                    })
                    .OrderBy(x => x.Time)
                    .Select(x => (JsonNode?)x.Obj)
                    .ToArray();
                return new JsonObject
                {
                    ["siteId"] = R(P(entry, "siteId")),
                    ["hostId"] = R(P(entry, "hostId")),
                    ["name"] = name,
                    ["points"] = new JsonArray(points),
                };
            }).ToList();
        }

        private static JsonArray BuildAlerts(List<JsonObject> sites, List<JsonObject> devices, List<JsonObject> isp)
        {
            var alerts = new List<(int Order, JsonObject Alert)>();
            void Add(string level, string text, string where) =>
                alerts.Add((level == "critical" ? 0 : level == "serious" ? 1 : 2, new JsonObject { ["level"] = level, ["text"] = text, ["where"] = where }));

            foreach (var d in devices)
            {
                if (ToStr(d["status"]) != "online" && !ToBool(d["placeholder"]))
                    Add("critical", $"{ToStr(d["name"])} is {ToStr(d["status"])}", ToStr(d["host"]));
            }
            foreach (var s in sites)
            {
                var name = ToStr(s["name"]);
                var wanUptime = ToNum(s["wanUptime"]);
                if (wanUptime != null && wanUptime < 99) Add("serious", $"WAN uptime {Fixed1(wanUptime.Value)}%", name);
                foreach (var w in s["wans"]!.AsArray())
                {
                    var wanIsp = ToStr(w!["isp"]);
                    var label = $"{ToStr(w["label"])} ({(wanIsp == "" ? "ISP" : wanIsp)})";
                    var issues = ToNum(w["issues"]) ?? 0;
                    if (!ToBool(w["up"])) Add("critical", $"{label} is down", name);
                    else if (issues > 0) Add("serious", $"{label} reports {issues} issue(s)", name);
                }
                var critical = ToNum(s["criticalNotifications"]) ?? 0;
                if (critical > 0) Add("critical", $"{critical} critical notification(s)", name);
            }
            foreach (var i in isp)
            {
                var points = i["points"]!.AsArray();
                if (points.Count == 0) continue;
                var last = points[^1]!;
                var loss = ToNum(last["loss"]);
                var latency = ToNum(last["latency"]);
                if (loss != null && loss >= 1) Add("serious", $"Packet loss {Fixed1(loss.Value)}%", ToStr(i["name"]));
                if (latency != null && latency >= 100) Add("warning", $"High latency {Math.Floor(latency.Value + 0.5)} ms", ToStr(i["name"]));
            }
            var updates = devices.Where(d => d["updateAvailable"] != null).ToList();
            if (updates.Count > 0)
                Add("warning", $"{updates.Count} device(s) have a firmware update",
                    string.Join(", ", updates.Take(3).Select(d => ToStr(d["name"]))) + (updates.Count > 3 ? "…" : ""));

            return new JsonArray(alerts.OrderBy(a => a.Order).Select(a => (JsonNode?)a.Alert).ToArray());
        }

        private JsonObject Summary()
        {
            var sites = NormaliseSites();
            var devices = NormaliseDevices();
            var isp = NormaliseIsp();
            HistorySample[] history;
            lock (_historyLock) history = _history.ToArray();

            return new JsonObject
            {
                ["updatedAt"] = _updatedAt,
                ["ispUpdatedAt"] = _ispUpdatedAt,
                ["refreshSeconds"] = RefreshSeconds,
                ["error"] = _error,
                ["sites"] = new JsonArray(sites.ToArray<JsonNode?>()),
                ["devices"] = new JsonArray(devices.ToArray<JsonNode?>()),
                ["isp"] = new JsonArray(isp.ToArray<JsonNode?>()),
                ["history"] = new JsonArray(history.Select(h => (JsonNode?)HistoryJson(h)).ToArray()),
                ["alerts"] = BuildAlerts(sites, devices, isp),
            };
        }

        public async Task<JsonObject> GetSummaryAsync()
        {
            await InitializeAsync();
            return Summary();
        }

        public async Task<JsonObject> RefreshAsync()
        {
            await InitializeAsync();
            await Task.WhenAll(RefreshCoreAsync(), RefreshIspAsync());
            return Summary();
        }

        // =====================================================================
        // History (client & device counts over time)
        // =====================================================================

        private static JsonObject HistoryJson(HistorySample h)
        {
            var sites = new JsonObject();
            foreach (var kv in h.Sites) sites[kv.Key] = new JsonArray(kv.Value.Select(v => N(v)).ToArray());
            return new JsonObject
            {
                ["t"] = Iso(h.T),
                ["wifi"] = h.Wifi,
                ["wired"] = h.Wired,
                ["guest"] = h.Guest,
                ["online"] = h.Online,
                ["offline"] = h.Offline,
                ["sites"] = sites,
            };
        }

        private static IReadOnlyList<KeyValuePair<string, double[]>> ParseSites(string? json)
        {
            var list = new List<KeyValuePair<string, double[]>>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            try
            {
                var el = JsonSerializer.Deserialize<JsonElement>(json);
                if (el.ValueKind != JsonValueKind.Object) return list;
                foreach (var p in el.EnumerateObject())
                    list.Add(new(p.Name, Arr(p.Value).Select(v => D(v) ?? 0).ToArray()));
            }
            catch (JsonException) { }
            return list;
        }

        private async Task RecordHistoryAsync()
        {
            var sites = NormaliseSites();
            var devices = NormaliseDevices().Where(d => !ToBool(d["placeholder"])).ToList();
            var online = devices.Count(d => ToStr(d["status"]) == "online");
            var sample = new HistorySample(
                DateTime.UtcNow,
                (int)sites.Sum(s => ToNum(s["wifiClients"]) ?? 0),
                (int)sites.Sum(s => ToNum(s["wiredClients"]) ?? 0),
                (int)sites.Sum(s => ToNum(s["guestClients"]) ?? 0),
                online,
                devices.Count - online,
                sites.Select(s => new KeyValuePair<string, double[]>(ToStr(s["id"]), new[]
                {
                    ToNum(s["wifiClients"]) ?? 0, ToNum(s["wiredClients"]) ?? 0, ToNum(s["guestClients"]) ?? 0
                })).ToList());

            var cutoff = DateTime.UtcNow.AddDays(-_historyDays);
            lock (_historyLock)
            {
                _history.Add(sample);
                _history.RemoveAll(h => h.T < cutoff);
            }

            await WithDbAsync(async db =>
            {
                await db.InsertUnifiHistoryDbAsync(sample.T, sample.Wifi, sample.Wired, sample.Guest, sample.Online, sample.Offline,
                    HistoryJson(sample)["sites"]!.ToJsonString(), RefreshSeconds - 10);
                if (DateTime.UtcNow - _lastHistoryCleanup > TimeSpan.FromHours(1))
                {
                    await db.DeleteUnifiHistoryOlderThanDbAsync(_historyDays);
                    _lastHistoryCleanup = DateTime.UtcNow;
                }
            });
        }

        private async Task<bool> WithDbAsync(Func<IDbRepository, Task> action)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await action(scope.ServiceProvider.GetRequiredService<IDbRepository>());
                return true;
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _dbWarned, 1) == 0)
                    _logger.LogWarning("UniFi history/alias tables unavailable ({Error}); dashboard data is kept in memory only.", ex.Message);
                return false;
            }
        }

        // =====================================================================
        // Console Network API (via the Site Manager connector)
        // =====================================================================

        private sealed class CacheEntry
        {
            public DateTime At;
            public bool HasValue;
            public object? Value;
            public Task<object?>? Pending;
        }

        private readonly Dictionary<string, CacheEntry> _netCache = new();
        private readonly object _cacheLock = new();

        // Cache with stale-while-revalidate: once a value exists, callers get it instantly
        // and an expired entry is refreshed in the background (one refresh at a time).
        private async Task<T> Cached<T>(string key, TimeSpan ttl, Func<Task<T>> fn)
        {
            Task<object?> pending;
            lock (_cacheLock)
            {
                _netCache.TryGetValue(key, out var hit);
                if (hit is { HasValue: true } && DateTime.UtcNow - hit.At < ttl) return (T)hit.Value!;
                if (hit?.Pending == null)
                {
                    var entry = hit ?? new CacheEntry();
                    _netCache[key] = entry;
                    entry.Pending = FillCacheAsync(key, entry, fn);
                    // Background refreshes must not raise unobserved exceptions.
                    entry.Pending.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    hit = entry;
                }
                if (hit.HasValue) return (T)hit.Value!;
                pending = hit.Pending!;
            }
            return (T)(await pending)!;
        }

        private async Task<object?> FillCacheAsync<T>(string key, CacheEntry entry, Func<Task<T>> fn)
        {
            await Task.Yield();
            try
            {
                var value = await fn();
                lock (_cacheLock)
                {
                    entry.At = DateTime.UtcNow;
                    entry.Value = value;
                    entry.HasValue = true;
                    entry.Pending = null;
                    _netCache[key] = entry;
                }
                return value;
            }
            catch
            {
                lock (_cacheLock)
                {
                    entry.Pending = null;
                    if (!entry.HasValue && _netCache.TryGetValue(key, out var cur) && cur == entry) _netCache.Remove(key);
                }
                throw;
            }
        }

        private void DropCache(params string[] keys)
        {
            lock (_cacheLock) foreach (var k in keys) _netCache.Remove(k);
        }

        private string? ConsoleId()
        {
            var hosts = _hosts;
            var host = Find(hosts, h => Truthy(P(h, "owner"))) ?? (hosts.Count > 0 ? hosts[0] : null);
            return Str(P(host, "id"));
        }

        private async Task<JsonElement> Net(string path, object? body = null)
        {
            if (!_client.IsConfigured) throw new InvalidOperationException("Unifi:ApiKey is not set in appsettings");
            // Requests that arrive before the first poll wait for the console list (up to 30s).
            if (ConsoleId() == null) await Task.WhenAny(_coreReady.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            var id = ConsoleId() ?? throw new InvalidOperationException("No UniFi console found for this API key");
            return await _client.NetworkAsync(id, path, body);
        }

        /// <summary>`(await net(...)).data || []`, optionally swallowing errors like `.catch(() => ({ data: [] }))`.</summary>
        private async Task<List<JsonElement>> NetData(string path, object? body = null, bool swallow = false)
        {
            try { return Arr(P(await Net(path, body), "data")).ToList(); }
            catch when (swallow) { return new List<JsonElement>(); }
        }

        private Task<List<JsonElement>> NetDevices() => Cached("devices", TimeSpan.FromSeconds(20), () => NetData("/api/s/default/stat/device"));
        private Task<List<JsonElement>> NetClients() => Cached("clients", TimeSpan.FromSeconds(20), () => NetData("/api/s/default/stat/sta"));

        private Task<List<LogEntry>> NetLogs() => Cached("logs", TimeSpan.FromSeconds(60), async () =>
        {
            var now = NowMs();
            Task<List<JsonElement>> Page(string cls, double from, int pageNumber, int size) =>
                NetData($"/v2/api/site/default/system-log/{cls}", new { timestampFrom = (long)from, timestampTo = (long)now, pageNumber, pageSize = size }, swallow: true);
            // Last 24h of everything (up to 3,000 events, pages fetched in parallel) + 30 days of device alerts.
            var pages = await Task.WhenAll(
                Page("all", now - DayMs, 0, 1000),
                Page("all", now - DayMs, 1, 1000),
                Page("all", now - DayMs, 2, 1000),
                Page("device-alert", now - 30 * DayMs, 0, 1000));
            var byId = new Dictionary<string, JsonElement>();
            var order = new List<string>();
            foreach (var e in pages.SelectMany(p => p))
            {
                var id = P(e, "id")?.GetRawText() ?? "undefined";
                if (!byId.ContainsKey(id)) order.Add(id);
                byId[id] = e;
            }
            return order.Select(id => NormaliseLog(byId[id])).OrderByDescending(l => l.TNum).ToList();
        });

        private static readonly Dictionary<string, string> Kind = new()
        {
            ["uap"] = "Access point", ["usw"] = "Switch", ["udm"] = "Console / Gateway", ["ugw"] = "Console / Gateway", ["uxg"] = "Console / Gateway", ["ucg"] = "Console / Gateway",
        };
        private static readonly Dictionary<int, string> State = new()
        {
            [0] = "offline", [1] = "online", [2] = "pending", [4] = "updating", [5] = "provisioning", [6] = "heartbeat missed",
            [7] = "adopting", [9] = "adoption failed", [10] = "isolated", [11] = "isolated",
        };
        private static readonly Dictionary<string, string> Band = new() { ["ng"] = "2.4 GHz", ["na"] = "5 GHz", ["6e"] = "6 GHz" };
        private static readonly string[] GatewayTypes = { "udm", "ugw", "uxg", "ucg" };

        private static JsonNode? BandOf(JsonElement? radio)
        {
            var r = Str(radio);
            return r != null && Band.TryGetValue(r, out var b) ? b : R(radio);
        }

        private static JsonNode? Satisfaction(JsonElement? v) => D(v) is double s && s >= 0 ? R(v) : null;

        private JsonObject NormaliseNetDevice(JsonElement d)
        {
            var sys = P(d, "system-stats");
            var radioCfg = Arr(P(d, "radio_table")).ToList();
            var radios = Arr(P(d, "radio_table_stats")).Select(r =>
            {
                var cfg = Find(radioCfg, x => Str(P(x, "name")) == Str(P(r, "name")));
                return (JsonNode?)new JsonObject
                {
                    ["band"] = BandOf(P(r, "radio")),
                    ["channel"] = R(P(r, "channel")),
                    ["width"] = R(P(cfg, "ht") ?? P(r, "bw")),
                    ["txPower"] = R(P(r, "tx_power")),
                    ["utilization"] = R(P(r, "cu_total")),
                    ["clients"] = R(P(r, "num_sta")) ?? N(0),
                    ["retries"] = R(P(r, "tx_retries_pct")),
                    ["satisfaction"] = Satisfaction(P(r, "satisfaction")),
                };
            }).ToArray();

            var ports = Arr(P(d, "port_table")).Select(p =>
            {
                var up = Truthy(P(p, "up"));
                return (JsonNode?)new JsonObject
                {
                    ["idx"] = R(P(p, "port_idx")),
                    ["name"] = Truthy(P(p, "name")) ? Str(P(p, "name")) : $"Port {Str(P(p, "port_idx")) ?? "undefined"}",
                    ["up"] = up,
                    ["enabled"] = !IsFalse(P(p, "enable")),
                    ["speed"] = up ? R(P(p, "speed")) : null,
                    ["media"] = S(P(p, "media")),
                    ["uplink"] = Truthy(P(p, "is_uplink")),
                    ["poe"] = Truthy(P(p, "poe_enable")) ? N(NumOrNull(P(p, "poe_power"))) : null,
                    ["txRate"] = R(P(p, "tx_bytes-r")),
                    ["rxRate"] = R(P(p, "rx_bytes-r")),
                    ["txBytes"] = R(P(p, "tx_bytes")),
                    ["rxBytes"] = R(P(p, "rx_bytes")),
                    ["errors"] = N((D(P(p, "rx_errors")) ?? 0) + (D(P(p, "tx_errors")) ?? 0)),
                };
            }).ToArray();

            var temps = Arr(P(d, "temperatures")).Select(t => (JsonNode?)new JsonObject { ["name"] = R(P(t, "name")), ["value"] = R(P(t, "value")) }).ToList();
            if (P(d, "general_temperature") != null && !Truthy(P(d, "temperatures")))
                temps.Add(new JsonObject { ["name"] = "General", ["value"] = R(P(d, "general_temperature")) });

            var type = Str(P(d, "type"));
            var placeholder = IsPlaceholder(P(d, "name"));
            var state = D(P(d, "state"));
            var uplink = P(d, "uplink");
            var userNumSta = P(d, "user-num_sta");

            return new JsonObject
            {
                ["mac"] = NormMac(Str(P(d, "mac"))),
                ["macDisplay"] = R(P(d, "mac")),
                ["name"] = S(P(d, "name"), P(d, "model"), P(d, "mac")),
                ["model"] = S(P(d, "model_name"), P(d, "shortname"), P(d, "model")),
                ["kind"] = R(P(d, "type")),
                ["type"] = type != null && Kind.TryGetValue(type, out var k) ? k : "Other",
                ["ip"] = S(P(d, "ip")),
                ["version"] = S(P(d, "displayable_version"), P(d, "version")),
                ["upgradeTo"] = Truthy(P(d, "upgradable")) ? (Truthy(P(d, "upgrade_to_firmware")) ? R(P(d, "upgrade_to_firmware")) : "available") : null,
                ["status"] = placeholder && state != 1 ? "not installed"
                    : state is double sv && sv == Math.Floor(sv) && State.TryGetValue((int)sv, out var stName) ? stName : "unknown",
                ["placeholder"] = placeholder,
                ["uptime"] = R(P(d, "uptime")),
                ["cpu"] = N(NumOrNull(P(sys, "cpu"))),
                ["mem"] = N(NumOrNull(P(sys, "mem"))),
                ["temps"] = new JsonArray(temps.ToArray()),
                ["clients"] = R(P(d, "num_sta")) ?? (Truthy(userNumSta) ? R(userNumSta) : N(0)),
                ["satisfaction"] = Satisfaction(P(d, "satisfaction")),
                ["txRate"] = R(P(d, "tx_bytes-r")),
                ["rxRate"] = R(P(d, "rx_bytes-r")),
                ["uplink"] = Truthy(uplink) ? new JsonObject
                {
                    ["mac"] = NormMac(Str(P(uplink, "uplink_mac"))),
                    ["name"] = S(P(uplink, "uplink_device_name")),
                    ["port"] = R(P(uplink, "uplink_remote_port")),
                    ["speed"] = R(P(uplink, "speed")),
                    ["type"] = S(P(uplink, "type")),
                } : null,
                ["serial"] = S(P(d, "serial")),
                ["lastSeen"] = Truthy(P(d, "last_seen")) ? N(D(P(d, "last_seen")) * 1000) : null,
                ["radios"] = new JsonArray(radios),
                ["ports"] = new JsonArray(ports),
            };
        }

        // =====================================================================
        // Client naming & type detection
        // =====================================================================

        private static readonly (Regex Re, string Name)[] Vendors =
        {
            (new Regex("hikvision", RegexOptions.IgnoreCase), "Hikvision"), (new Regex("dahua", RegexOptions.IgnoreCase), "Dahua"),
            (new Regex("ezviz", RegexOptions.IgnoreCase), "EZVIZ"), (new Regex("apple", RegexOptions.IgnoreCase), "Apple"),
            (new Regex("samsung", RegexOptions.IgnoreCase), "Samsung"), (new Regex(@"hewlett|\bhp\b", RegexOptions.IgnoreCase), "HP"),
            (new Regex("dell", RegexOptions.IgnoreCase), "Dell"), (new Regex("lenovo", RegexOptions.IgnoreCase), "Lenovo"),
            (new Regex("intel", RegexOptions.IgnoreCase), "Intel"), (new Regex("realtek", RegexOptions.IgnoreCase), "Realtek"),
            (new Regex("oppo", RegexOptions.IgnoreCase), "OPPO"), (new Regex("xiaomi", RegexOptions.IgnoreCase), "Xiaomi"),
            (new Regex("vivo", RegexOptions.IgnoreCase), "vivo"), (new Regex("infinix", RegexOptions.IgnoreCase), "Infinix"),
            (new Regex("huawei", RegexOptions.IgnoreCase), "Huawei"), (new Regex("tp-?link", RegexOptions.IgnoreCase), "TP-Link"),
            (new Regex("ubiquiti", RegexOptions.IgnoreCase), "Ubiquiti"), (new Regex("microsoft", RegexOptions.IgnoreCase), "Microsoft"),
            (new Regex("google", RegexOptions.IgnoreCase), "Google"), (new Regex("amazon", RegexOptions.IgnoreCase), "Amazon"),
            (new Regex("azurewave|liteon|hon hai|foxconn|murata|universal global", RegexOptions.IgnoreCase), "PC"),
        };
        private static readonly Regex VendorSuffixRe = new(@"[,.]?\s*(co|corp|corporation|inc|ltd|limited|llc|gmbh|technology|technologies|electronics|digital)\b.*$", RegexOptions.IgnoreCase);

        private static string ShortVendor(string? oui)
        {
            if (string.IsNullOrEmpty(oui)) return "";
            foreach (var (re, name) in Vendors) if (re.IsMatch(oui)) return name;
            return string.Join(" ", Regex.Split(VendorSuffixRe.Replace(oui, ""), @"\s+").Take(2));
        }

        private static readonly Dictionary<string, string> Kinds = new()
        {
            ["phone"] = "Phone", ["computer"] = "Computer", ["tablet"] = "Tablet", ["printer"] = "Printer", ["tv"] = "TV / media",
            ["camera"] = "Camera", ["access"] = "Access control", ["network"] = "Network device", ["iot"] = "Smart device", ["other"] = "Device",
        };

        private static readonly Regex AccessNameRe = new(@"^access\b|door|turnstile|attendance");
        private static readonly Regex PhoneNameRe = new(@"iphone|galaxy|redmi|oppo|tecno|infinix|vivo|realme|pixel|android|huawei|honor|nokia|oneplus|poco|\bsm-|\bnote\b|-s-a\d|reno|spark");
        private static readonly Regex TabletNameRe = new(@"ipad|\btab\b|tablet");
        private static readonly Regex ComputerNameRe = new(@"macbook|imac|desktop-|laptop-|thinkpad|\bpc\b|-pc\b|workstation|surface");
        private static readonly Regex PrinterNameRe = new(@"printer|laserjet|officejet|epson|canon|brother|^hp[0-9a-f]{6}");
        private static readonly Regex TvNameRe = new(@"\btv\b|bravia|chromecast|roku|fire.?tv|apple-tv|android-tv|projector");
        private static readonly Regex NetworkNameRe = new(@"tl-|router|switch|\bap\b|extender|mesh");
        private static readonly Regex CameraNameRe = new(@"cam|nvr|dvr|ipc");
        private static readonly Regex CameraVendorRe = new(@"hikvision|dahua|ezviz|uniview");
        private static readonly Regex IotVendorRe = new(@"espressif|tuya|shelly|sonoff");

        private static string ClientKind(JsonElement c)
        {
            var n = $"{S(P(c, "name"))} {S(P(c, "hostname"))}".ToLowerInvariant();
            var v = S(P(c, "oui")).ToLowerInvariant();
            if (AccessNameRe.IsMatch(n)) return "access";
            if (PhoneNameRe.IsMatch(n)) return "phone";
            if (TabletNameRe.IsMatch(n)) return "tablet";
            if (ComputerNameRe.IsMatch(n)) return "computer";
            if (PrinterNameRe.IsMatch(n)) return "printer";
            if (TvNameRe.IsMatch(n)) return "tv";
            if (NetworkNameRe.IsMatch(n)) return "network";
            if (CameraNameRe.IsMatch(n) || CameraVendorRe.IsMatch(v)) return "camera";
            // UniFi fingerprint categories seen on this network.
            var devCat = D(P(c, "dev_cat"));
            if (devCat == 44 || devCat == 6) return "phone";
            if (devCat == 1) return "computer";
            if (devCat == 49) return "printer";
            if (devCat == 57) return "access";
            if (IotVendorRe.IsMatch(v)) return "iot";
            return "other";
        }

        // Locally administered MAC = randomised "private address" (phones do this by default).
        private static bool IsPrivateMac(string? mac) => Regex.IsMatch(mac ?? "", "^.[26ae]", RegexOptions.IgnoreCase);

        private string AliasOf(string? mac) => _aliases.TryGetValue(NormMac(mac), out var a) ? a : "";

        private string FriendlyName(JsonElement c, string kind)
        {
            var mac = Truthy(P(c, "mac")) ? Str(P(c, "mac")) ?? "" : "";
            var alias = AliasOf(mac);
            if (alias != "") return alias;
            if (Truthy(P(c, "name"))) return Str(P(c, "name"))!;
            if (Truthy(P(c, "hostname"))) return Str(P(c, "hostname"))!;
            var tail = mac.Length > 5 ? mac[^5..] : mac;
            var word = kind == "other" ? "device" : Kinds[kind].ToLowerInvariant();
            var vendor = ShortVendor(Str(P(c, "oui")));
            if (vendor != "") return $"{vendor} {word} · {tail}";
            if (IsPrivateMac(mac)) return $"Private {word} · {tail}";
            return $"Unknown device · {tail}";
        }

        private static string UplinkMacOf(JsonElement c) =>
            NormMac(Truthy(P(c, "is_wired")) ? S(P(c, "sw_mac"), P(c, "last_uplink_mac")) : S(P(c, "ap_mac"), P(c, "last_uplink_mac")));

        private JsonObject NormaliseClient(JsonElement c, Dictionary<string, JsonObject> devByMac)
        {
            var wired = Truthy(P(c, "is_wired"));
            var uplinkMac = UplinkMacOf(c);
            devByMac.TryGetValue(uplinkMac, out var up);
            var tx = D(wired ? P(c, "wired-tx_bytes") : P(c, "tx_bytes"));
            var rx = D(wired ? P(c, "wired-rx_bytes") : P(c, "rx_bytes"));
            var kind = ClientKind(c);
            var mac = Str(P(c, "mac"));
            var upName = up != null ? ToStr(up["name"]) : "";
            var radio = P(c, "radio");

            return new JsonObject
            {
                ["mac"] = NormMac(mac),
                ["macDisplay"] = R(P(c, "mac")),
                ["name"] = FriendlyName(c, kind),
                ["alias"] = AliasOf(mac),
                ["unifiName"] = S(P(c, "name")),
                ["kind"] = kind,
                ["kindLabel"] = Kinds[kind],
                ["privateMac"] = IsPrivateMac(mac),
                ["hostname"] = S(P(c, "hostname")),
                ["vendor"] = S(P(c, "oui")),
                ["ip"] = S(P(c, "ip"), P(c, "last_ip")),
                ["wired"] = wired,
                ["guest"] = Truthy(P(c, "is_guest")),
                ["network"] = S(P(c, "network"), P(c, "last_connection_network_name")),
                ["vlan"] = R(P(c, "vlan")),
                ["ssid"] = S(P(c, "essid")),
                ["uplinkMac"] = uplinkMac,
                ["uplinkName"] = upName != "" ? upName : S(P(c, "last_uplink_name")),
                ["port"] = wired ? R(P(c, "sw_port") ?? P(c, "last_uplink_remote_port")) : null,
                ["signal"] = wired ? null : R(P(c, "signal")),
                ["band"] = wired ? "" : (Str(radio) is string rb && Band.TryGetValue(rb, out var b) ? b : S(radio)),
                ["channel"] = wired ? null : R(P(c, "channel")),
                ["txRate"] = Truthy(P(c, "tx_rate")) ? N(D(P(c, "tx_rate")) / 1000) : null,
                ["rxRate"] = Truthy(P(c, "rx_rate")) ? N(D(P(c, "rx_rate")) / 1000) : null,
                ["experience"] = R(P(c, "satisfaction") ?? P(c, "satisfaction_real")),
                ["uptime"] = R(P(c, "uptime")),
                ["dataBytes"] = N((tx ?? 0) + (rx ?? 0)),
                ["firstSeen"] = Truthy(P(c, "first_seen")) ? N(D(P(c, "first_seen")) * 1000) : null,
            };
        }

        // =====================================================================
        // Event log
        // =====================================================================

        private sealed record LogEntry(JsonElement? Id, JsonElement? T, double TNum, string Event, string? Conn, string Title, string Message,
                                       string Severity, string Category, List<string> Macs);

        private static readonly Regex LogTokenRe = new(@"\{([A-Z_]+)\}");
        private static readonly Regex ConnDownRe = new("UNREACHABLE|DISCONNECT|OFFLINE|LOST");
        private static readonly Regex ConnUpRe = new("RECONNECT|CONNECTED|ROAMED|ONLINE|ADOPTED");

        private static LogEntry NormaliseLog(JsonElement e)
        {
            var p = P(e, "parameters");
            string Fill(JsonElement? str) => LogTokenRe.Replace(Truthy(str) ? Str(str) ?? "" : "", m =>
            {
                var param = P(p, m.Groups[1].Value);
                return Str(P(param, "name")) ?? Str(P(param, "id")) ?? m.Value;
            });

            var macs = new List<JsonElement?> { P(p, "DEVICE", "id"), P(p, "CLIENT", "id") };
            macs.AddRange(Arr(P(p, "DEVICES", "devices")).Select(d => P(d, "mac")));
            var eventName = S(P(e, "event"), P(e, "key"));
            // Connectivity direction, used to draw online/offline timelines. Check "down" first: DISCONNECTED contains CONNECTED.
            var conn = ConnDownRe.IsMatch(eventName) ? "down" : ConnUpRe.IsMatch(eventName) ? "up" : null;

            var title = Fill(P(e, "title_raw"));
            if (title == "") title = S(P(e, "key"), P(e, "event"));
            var message = Truthy(P(e, "message")) ? Str(P(e, "message"))! : Fill(P(e, "message_raw"));

            return new LogEntry(
                P(e, "id"),
                P(e, "timestamp"),
                D(P(e, "timestamp")) ?? 0,
                eventName,
                conn,
                title,
                message,
                S(P(e, "severity")).ToLowerInvariant(),
                S(P(e, "category")).Replace("_", " ").ToLowerInvariant(),
                macs.Where(Truthy).Select(m => NormMac(Str(m))).Distinct().ToList());
        }

        private static JsonObject LogJson(LogEntry l) => new()
        {
            ["id"] = R(l.Id),
            ["t"] = R(l.T),
            ["event"] = l.Event,
            ["conn"] = l.Conn,
            ["title"] = l.Title,
            ["message"] = l.Message,
            ["severity"] = l.Severity,
            ["category"] = l.Category,
            ["macs"] = new JsonArray(l.Macs.Select(m => (JsonNode?)m).ToArray()),
        };

        public async Task<JsonObject> GetLogsAsync(string? mac)
        {
            var m = NormMac(mac);
            var logs = await NetLogs();
            return new JsonObject
            {
                ["logs"] = new JsonArray((m != "" ? logs.Where(l => l.Macs.Contains(m)) : logs).Take(500).Select(l => (JsonNode?)LogJson(l)).ToArray()),
            };
        }

        // =====================================================================
        // Clients, devices, topology
        // =====================================================================

        public async Task<JsonObject> GetClientsAsync()
        {
            await InitializeAsync();
            var devicesTask = NetDevices();
            var clientsTask = NetClients();
            await Task.WhenAll(devicesTask, clientsTask);
            var devs = devicesTask.Result.Select(NormaliseNetDevice).ToList();
            var devByMac = DevByMac(devs);
            return new JsonObject
            {
                ["clients"] = new JsonArray(clientsTask.Result.Select(c => (JsonNode?)NormaliseClient(c, devByMac)).ToArray()),
                ["devices"] = new JsonArray(devs.Select(d => (JsonNode?)new JsonObject
                {
                    ["mac"] = d["mac"]?.DeepClone(),
                    ["name"] = d["name"]?.DeepClone(),
                    ["type"] = d["type"]?.DeepClone(),
                }).ToArray()),
            };
        }

        public async Task<JsonObject?> GetDeviceAsync(string mac)
        {
            await InitializeAsync();
            mac = NormMac(mac);
            var devicesTask = NetDevices();
            var clientsTask = NetClients();
            var logsTask = NetLogs();
            await Task.WhenAll(devicesTask, clientsTask, logsTask);
            var devs = devicesTask.Result.Select(NormaliseNetDevice).ToList();
            var device = devs.FirstOrDefault(d => ToStr(d["mac"]) == mac);
            if (device == null) return null;
            var devByMac = DevByMac(devs);
            return new JsonObject
            {
                ["device"] = device,
                ["clients"] = new JsonArray(clientsTask.Result.Select(c => NormaliseClient(c, devByMac)).Where(c => ToStr(c["uplinkMac"]) == mac).Select(c => (JsonNode?)c).ToArray()),
                ["logs"] = new JsonArray(logsTask.Result.Where(l => l.Macs.Contains(mac)).Take(500).Select(l => (JsonNode?)LogJson(l)).ToArray()),
            };
        }

        public async Task<JsonObject?> GetDeviceHistoryAsync(string mac, int rangeHours)
        {
            mac = NormMac(mac);
            var found = Find(await NetDevices(), x => NormMac(Str(P(x, "mac"))) == mac);
            if (found is not { } d) return null;
            var type = Str(P(d, "type"));
            var kind = type == "uap" ? "ap" : type == "usw" ? "sw" : "gw";
            var hourly = rangeHours > 24;
            var secs = hourly ? 3600 : 300;
            var attrs = kind switch
            {
                "ap" => new[] { "time", "num_sta", "tx_bytes", "rx_bytes", "cpu", "mem" },
                "sw" => new[] { "time", "tx_bytes", "rx_bytes", "cpu", "mem" },
                _ => new[] { "time", "cpu", "mem", "wan-tx_bytes", "wan-rx_bytes", "lan-num_sta", "wlan-num_sta" },
            };
            var end = NowMs();
            var rows = await Cached($"hist:{mac}:{rangeHours}", TimeSpan.FromSeconds(60), () =>
                NetData($"/api/s/default/stat/report/{(hourly ? "hourly" : "5minutes")}.{kind}",
                    new { attrs, start = (long)(end - rangeHours * HourMs), end = (long)end, macs = new[] { Str(P(d, "mac")) } }));
            JsonNode? Mbps(JsonElement? b) => D(b) is double v ? N(v * 8 / secs / 1e6) : null;

            return new JsonObject
            {
                ["kind"] = kind,
                ["points"] = new JsonArray(rows.OrderBy(r => D(P(r, "time")) ?? 0).Select(r => (JsonNode?)new JsonObject
                {
                    ["t"] = R(P(r, "time")),
                    ["clients"] = kind == "gw" ? N((D(P(r, "lan-num_sta")) ?? 0) + (D(P(r, "wlan-num_sta")) ?? 0)) : R(P(r, "num_sta")),
                    // Download = toward clients/LAN for APs & switches, from the internet for the gateway.
                    ["down"] = Mbps(kind == "gw" ? P(r, "wan-rx_bytes") : P(r, "tx_bytes")),
                    ["up"] = Mbps(kind == "gw" ? P(r, "wan-tx_bytes") : P(r, "rx_bytes")),
                    ["cpu"] = R(P(r, "cpu")),
                    ["mem"] = R(P(r, "mem")),
                }).ToArray()),
            };
        }

        public async Task<JsonObject> GetTopologyAsync()
        {
            var devicesTask = NetDevices();
            var clientsTask = NetClients();
            await Task.WhenAll(devicesTask, clientsTask);
            var devs = devicesTask.Result.Select(NormaliseNetDevice).ToList();
            var count = new Dictionary<string, int>();
            foreach (var c in clientsTask.Result)
            {
                var up = UplinkMacOf(c);
                count[up] = count.GetValueOrDefault(up) + 1;
            }
            var known = devs.Select(d => ToStr(d["mac"])).ToHashSet();
            var gw = devs.FirstOrDefault(d => ToStr(d["type"]) == "Console / Gateway");
            var gwMac = gw != null ? ToStr(gw["mac"]) : null;

            var nodes = devs.Select(d =>
            {
                var uplink = d["uplink"];
                var mac = ToStr(d["mac"]);
                return new JsonObject
                {
                    ["mac"] = mac,
                    ["name"] = d["name"]?.DeepClone(),
                    ["model"] = d["model"]?.DeepClone(),
                    ["type"] = d["type"]?.DeepClone(),
                    ["kind"] = d["kind"]?.DeepClone(),
                    ["status"] = d["status"]?.DeepClone(),
                    ["placeholder"] = d["placeholder"]?.DeepClone(),
                    ["clients"] = count.GetValueOrDefault(mac),
                    ["uplinkMac"] = uplink != null ? ToStr(uplink["mac"]) : "",
                    ["uplinkPort"] = uplink?["port"]?.DeepClone(),
                    ["speed"] = uplink?["speed"]?.DeepClone(),
                };
            }).ToList();

            // Uplinks to MACs UniFi doesn't manage are third-party switches; show them as their own nodes under the gateway.
            var gwNode = nodes.FirstOrDefault(x => gwMac != null && ToStr(x["mac"]) == gwMac);
            for (var i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                var uplinkMac = ToStr(n["uplinkMac"]);
                if (n == gwNode) { n["uplinkMac"] = ""; continue; }
                if (uplinkMac == "") { n["uplinkMac"] = gwMac ?? ""; n["uplinkUnknown"] = true; continue; }
                if (known.Contains(uplinkMac)) continue;
                if (!nodes.Any(x => ToStr(x["mac"]) == uplinkMac))
                {
                    var tail = uplinkMac.Length > 4 ? uplinkMac[^4..] : uplinkMac;
                    tail = Regex.Replace(tail, "(..)(..)", "$1:$2");
                    nodes.Add(new JsonObject
                    {
                        ["mac"] = uplinkMac,
                        ["name"] = $"Third-party switch · {tail}",
                        ["model"] = "Not managed by UniFi",
                        ["type"] = "Third-party switch",
                        ["kind"] = "external",
                        ["status"] = "unknown",
                        ["virtual"] = true,
                        ["clients"] = 0,
                        ["uplinkMac"] = gwMac ?? "",
                        ["uplinkUnknown"] = true,
                    });
                }
            }
            return new JsonObject { ["nodes"] = new JsonArray(nodes.ToArray<JsonNode?>()) };
        }

        // =====================================================================
        // WANs (live), WAN traffic, speed-test archive
        // =====================================================================

        private async Task<JsonElement?> GatewayDevice() => Find(await NetDevices(), d => GatewayTypes.Contains(Str(P(d, "type"))));
        private static string WanPrefix(string k) => k == "wan1" ? "wan" : k; // report attribute prefix per WAN port
        private static List<string> WanKeysOf(JsonElement gw) =>
            gw.EnumerateObject().Select(p => p.Name).Where(k => Regex.IsMatch(k, @"^wan\d$")).OrderBy(k => k, StringComparer.Ordinal).ToList();

        private Task<List<JsonElement>> SpeedtestArchive() => Cached("speedtests", TimeSpan.FromMinutes(10), () =>
        {
            var end = NowMs();
            return NetData("/api/s/default/stat/report/archive.speedtest",
                new { attrs = new[] { "xput_download", "xput_upload", "latency", "time", "interface_name" }, start = (long)(end - 90 * DayMs), end = (long)end },
                swallow: true);
        });

        public async Task<JsonObject> GetWanTrafficAsync(int rangeHours)
        {
            var gw = await GatewayDevice();
            if (gw is { } g) return (await GetDeviceHistoryAsync(NormMac(Str(P(g, "mac"))), rangeHours))!;
            return new JsonObject { ["kind"] = "gw", ["points"] = new JsonArray() };
        }

        // Live speed, link, latency, last speed test and 24h traffic for each WAN port on the gateway.
        public async Task<JsonObject> GetWansAsync()
        {
            if (await GatewayDevice() is not { } gw) return new JsonObject { ["wans"] = new JsonArray() };
            var keys = WanKeysOf(gw);
            var end = NowMs();
            var rows = await Cached("wan-hist", TimeSpan.FromSeconds(120), () =>
                NetData("/api/s/default/stat/report/5minutes.gw",
                    new
                    {
                        attrs = new[] { "time" }.Concat(keys.SelectMany(k => new[] { $"{WanPrefix(k)}-rx_bytes", $"{WanPrefix(k)}-tx_bytes" })).ToArray(),
                        start = (long)(end - DayMs), end = (long)end, macs = new[] { Str(P(gw, "mac")) },
                    }, swallow: true));
            var st = P(gw, "speedtest-status");
            JsonNode? Mbps(JsonElement? b) => D(b) is double v ? N(v * 8 / 300 / 1e6) : null;
            // Speed-test history (90 days): the gateway only keeps the latest run for the primary WAN, the archive has all of them.
            var tests = await SpeedtestArchive();

            var wans = keys.Select(k =>
            {
                var w = P(gw, k);
                var p = WanPrefix(k);
                var ifname = Str(P(w, "ifname"));
                var tested = Truthy(P(st, "interface_name")) && Str(P(st, "interface_name")) == ifname;

                // Latest archived run for this interface.
                var runs = tests.Where(t => Str(P(t, "interface_name")) == ifname).OrderByDescending(t => D(P(t, "time")) ?? 0).ToList();
                var histAt = runs.Count > 0 ? D(P(runs[0], "time")) : null;
                var histCount = runs.Count;
                // Prefer the live status (newest, has the server name); fall back to the archive.
                var liveAt = (D(P(st, "rundate")) ?? 0) * 1000;
                var hasLive = tested && Truthy(P(st, "xput_download"));
                JsonNode? speedtest;
                if (hasLive && (runs.Count == 0 || liveAt >= (histAt ?? double.NaN)))
                {
                    speedtest = new JsonObject
                    {
                        ["down"] = R(P(st, "xput_download")),
                        ["up"] = R(P(st, "xput_upload")),
                        ["ping"] = R(P(st, "latency")),
                        ["at"] = N(liveAt),
                        ["server"] = S(P(st, "server", "provider")),
                        ["count"] = histCount > 0 ? histCount : 1,
                    };
                }
                else
                {
                    speedtest = runs.Count > 0 ? new JsonObject
                    {
                        ["down"] = R(P(runs[0], "xput_download")),
                        ["up"] = R(P(runs[0], "xput_upload")),
                        ["ping"] = R(P(runs[0], "latency")),
                        ["at"] = R(P(runs[0], "time")),
                        ["server"] = "",
                        ["count"] = histCount,
                    } : null;
                }

                return (JsonNode?)new JsonObject
                {
                    ["key"] = k.ToUpperInvariant(), // WAN1 / WAN2 / WAN3 — matches the overview cards
                    ["ifname"] = ifname ?? "",
                    ["up"] = Truthy(P(w, "up")),
                    ["linkSpeed"] = R(P(w, "speed")), // Mbps
                    ["downRate"] = R(P(w, "rx_bytes-r")), // bytes/s
                    ["upRate"] = R(P(w, "tx_bytes-r")),
                    ["latency"] = R(P(w, "latency")),
                    ["availability"] = R(P(w, "availability")),
                    ["downTotal"] = R(P(w, "rx_bytes")),
                    ["upTotal"] = R(P(w, "tx_bytes")),
                    ["speedtest"] = speedtest,
                    ["history"] = new JsonArray(rows.OrderBy(r => D(P(r, "time")) ?? 0).Select(r => (JsonNode?)new JsonObject
                    {
                        ["t"] = R(P(r, "time")),
                        ["down"] = Mbps(P(r, $"{p}-rx_bytes")),
                        ["up"] = Mbps(P(r, $"{p}-tx_bytes")),
                    }).ToArray()),
                };
            }).ToArray();

            return new JsonObject { ["wans"] = new JsonArray(wans) };
        }

        // =====================================================================
        // Internet: ISP comparison & failover history
        // =====================================================================

        private sealed record GwRow(double T, int Secs, List<(string Key, double Down, double Up)> Wans);

        private static JsonObject WansJson(GwRow r)
        {
            var o = new JsonObject();
            foreach (var w in r.Wans) o[w.Key] = new JsonObject { ["down"] = N(w.Down), ["up"] = N(w.Up) };
            return o;
        }

        private async Task<List<GwRow>> GwTraffic(string interval, int days)
        {
            if (await GatewayDevice() is not { } gw) return new List<GwRow>();
            var keys = WanKeysOf(gw);
            var end = NowMs();
            var secs = interval == "daily" ? 86400 : 3600;
            return await Cached($"gw-{interval}-{days}", interval == "daily" ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(10), async () =>
            {
                var rows = await NetData($"/api/s/default/stat/report/{interval}.gw",
                    new
                    {
                        attrs = new[] { "time" }.Concat(keys.SelectMany(k => new[] { $"{WanPrefix(k)}-rx_bytes", $"{WanPrefix(k)}-tx_bytes" })).ToArray(),
                        start = (long)(end - days * DayMs), end = (long)end, macs = new[] { Str(P(gw, "mac")) },
                    });
                return rows.Select(r => new GwRow(
                        D(P(r, "time")) ?? 0,
                        secs,
                        keys.Select(k => (k.ToUpperInvariant(), D(P(r, $"{WanPrefix(k)}-rx_bytes")) ?? 0, D(P(r, $"{WanPrefix(k)}-tx_bytes")) ?? 0)).ToList()))
                    .OrderBy(r => r.T).ToList();
            });
        }

        // A bucket is "on backup" when the backups carried most of the traffic (and there was real traffic).
        private static List<(double From, double To, List<string> Via)> FailoverEpisodes(IEnumerable<GwRow> rows, string primary = "WAN1")
        {
            var eps = new List<(double From, double To, List<string> Via)>();
            var curIndex = -1;
            foreach (var r in rows)
            {
                var total = r.Wans.Sum(w => w.Down + w.Up);
                var prim = r.Wans.Where(w => w.Key == primary).Select(w => w.Down + w.Up).FirstOrDefault();
                var avgMbps = total * 8 / r.Secs / 1e6;
                var backup = r.Wans.Where(w => w.Key != primary).OrderByDescending(w => w.Down + w.Up).Select(w => w.Key).FirstOrDefault();
                var on = avgMbps > 0.05 && total - prim > total * 0.5;
                if (!on) continue;
                if (curIndex >= 0 && eps[curIndex].To == r.T)
                {
                    var cur = eps[curIndex];
                    if (backup != null && !cur.Via.Contains(backup)) cur.Via.Add(backup);
                    eps[curIndex] = (cur.From, r.T + r.Secs * 1000.0, cur.Via);
                }
                else
                {
                    eps.Add((r.T, r.T + r.Secs * 1000.0, backup != null ? new List<string> { backup } : new List<string>()));
                    curIndex = eps.Count - 1;
                }
            }
            return eps;
        }

        public async Task<JsonObject> GetIspAsync()
        {
            if (await GatewayDevice() is not { } gw) return new JsonObject { ["wans"] = new JsonArray() };
            var testsTask = SpeedtestArchive();
            var hourlyTask = GwTraffic("hourly", 7);
            var dailyTask = GwTraffic("daily", 60);
            await Task.WhenAll(testsTask, hourlyTask, dailyTask);
            var (tests, hourly, daily) = (testsTask.Result, hourlyTask.Result, dailyTask.Result);

            var site = NormaliseSites().FirstOrDefault();
            var statGw = P(gw, "stat", "gw");
            double Sum(IEnumerable<GwRow> rows, string key, bool down) =>
                rows.Sum(r => r.Wans.Where(w => w.Key == key).Select(w => down ? w.Down : w.Up).FirstOrDefault());
            var now = NowMs();
            var last30 = daily.Where(r => r.T >= now - 30 * DayMs).ToList();

            var wans = WanKeysOf(gw).Select(k =>
            {
                var key = k.ToUpperInvariant();
                var port = P(gw, k);
                var cloud = site?["wans"]!.AsArray().FirstOrDefault(w => ToStr(w!["label"]) == key);
                var up = P(gw, "uptime_stats", key == "WAN1" ? "WAN" : key);
                var ifname = Str(P(port, "ifname"));
                var runs = tests.Where(t => Str(P(t, "interface_name")) == ifname).OrderBy(t => D(P(t, "time")) ?? 0).ToList();
                double? Avg(string f) => runs.Count > 0 ? runs.Sum(t => D(P(t, f)) ?? 0) / runs.Count : null;
                var latest = runs.Count > 0 ? runs[^1] : (JsonElement?)null;

                return (JsonNode?)new JsonObject
                {
                    ["key"] = key,
                    ["isp"] = cloud != null ? ToStr(cloud["isp"]) : "",
                    ["organization"] = cloud != null ? ToStr(cloud["organization"]) : "",
                    ["priority"] = cloud?["priority"]?.DeepClone(),
                    ["up"] = Truthy(P(port, "up")),
                    ["ifname"] = ifname ?? "",
                    ["linkSpeed"] = R(P(port, "speed")),
                    ["availability"] = R(P(up, "availability") ?? P(port, "availability")),
                    ["latency"] = R(P(up, "latency_average") ?? P(port, "latency")),
                    ["connectedFor"] = R(P(up, "uptime")), // seconds since this WAN last (re)connected
                    ["downtimeSec"] = R(P(statGw, $"{WanPrefix(k)}-downtime")), // since the gateway started
                    ["tests"] = new JsonObject
                    {
                        ["count"] = runs.Count,
                        ["latest"] = latest is { } l ? new JsonObject
                        {
                            ["down"] = R(P(l, "xput_download")),
                            ["up"] = R(P(l, "xput_upload")),
                            ["ping"] = R(P(l, "latency")),
                            ["at"] = R(P(l, "time")),
                        } : null,
                        ["avgDown"] = N(Avg("xput_download")),
                        ["avgUp"] = N(Avg("xput_upload")),
                        ["avgPing"] = N(Avg("latency")),
                        ["bestDown"] = runs.Count > 0 ? N(runs.Max(t => D(P(t, "xput_download")) ?? 0)) : null,
                        ["history"] = new JsonArray(runs.Select(t => (JsonNode?)new JsonObject
                        {
                            ["t"] = R(P(t, "time")),
                            ["down"] = R(P(t, "xput_download")),
                            ["up"] = R(P(t, "xput_upload")),
                            ["ping"] = R(P(t, "latency")),
                        }).ToArray()),
                    },
                    ["traffic7d"] = new JsonObject { ["down"] = N(Sum(hourly, key, true)), ["up"] = N(Sum(hourly, key, false)) },
                    ["traffic30d"] = new JsonObject { ["down"] = N(Sum(last30, key, true)), ["up"] = N(Sum(last30, key, false)) },
                };
            }).ToArray();

            // Failovers: hourly detail for the last 7 days, daily resolution before that.
            var hourlyFrom = hourly.Count > 0 ? hourly[0].T : now;
            var episodes = FailoverEpisodes(daily.Where(r => r.T + DayMs <= hourlyFrom)).Concat(FailoverEpisodes(hourly));

            return new JsonObject
            {
                ["gatewayUptime"] = R(P(gw, "uptime")),
                ["failoverSeconds"] = R(P(statGw, "failover-seconds") ?? P(statGw, "wan2-failover")),
                ["mode"] = site != null ? ToStr(site["loadBalancing"]) : "",
                ["wans"] = new JsonArray(wans),
                ["episodes"] = new JsonArray(episodes.Select(e => (JsonNode?)new JsonObject
                {
                    ["from"] = N(e.From),
                    ["to"] = N(e.To),
                    ["via"] = new JsonArray(e.Via.Select(v => (JsonNode?)v).ToArray()),
                }).ToArray()),
                ["hourly"] = new JsonArray(hourly.Select(r => (JsonNode?)new JsonObject { ["t"] = N(r.T), ["secs"] = r.Secs, ["wans"] = WansJson(r) }).ToArray()),
                ["daily"] = new JsonArray(daily.Select(r => (JsonNode?)new JsonObject { ["t"] = N(r.T), ["secs"] = r.Secs, ["wans"] = WansJson(r) }).ToArray()),
            };
        }

        // =====================================================================
        // Wi-Fi health
        // =====================================================================

        public async Task<JsonObject> GetWifiAsync()
        {
            var devices = await NetDevices();
            var aps = devices.Where(d => Str(P(d, "type")) == "uap").ToList();
            var end = NowMs();
            var hist = await Cached("ap-hourly", TimeSpan.FromMinutes(10), async () =>
            {
                var rows = await NetData("/api/s/default/stat/report/hourly.ap",
                    new
                    {
                        attrs = new[] { "time", "num_sta", "satisfaction", "tx_retries", "tx_packets", "bytes" },
                        start = (long)(end - 24 * HourMs), end = (long)end, macs = aps.Select(a => Str(P(a, "mac"))).ToArray(),
                    }, swallow: true);
                _logger.LogInformation("UniFi hourly AP report: {Rows} rows, {WithScore} with an experience score", rows.Count, rows.Count(r => P(r, "satisfaction") != null));
                return rows;
            });

            return new JsonObject
            {
                ["aps"] = new JsonArray(aps.Select(d =>
                {
                    var n = NormaliseNetDevice(d);
                    var radioCfg = Arr(P(d, "radio_table")).ToList();
                    var radios = Arr(P(d, "radio_table_stats")).Select(r =>
                    {
                        var cfg = Find(radioCfg, x => Str(P(x, "name")) == Str(P(r, "name")));
                        var self = (D(P(r, "cu_self_rx")) ?? 0) + (D(P(r, "cu_self_tx")) ?? 0);
                        var cuTotal = D(P(r, "cu_total"));
                        return (JsonNode?)new JsonObject
                        {
                            ["band"] = BandOf(P(r, "radio")),
                            ["channel"] = R(P(r, "channel")),
                            ["width"] = R(P(cfg, "ht") ?? P(r, "bw")),
                            ["txPower"] = R(P(r, "tx_power")),
                            ["utilization"] = R(P(r, "cu_total")), // % airtime busy
                            ["interference"] = cuTotal != null ? N(Math.Max(0, cuTotal.Value - self)) : null, // busy, but not from this AP
                            ["retries"] = R(P(r, "tx_retries_pct")),
                            ["clients"] = R(P(r, "num_sta")) ?? N(0),
                            ["satisfaction"] = Satisfaction(P(r, "satisfaction")),
                        };
                    }).ToArray();
                    var mac = NormMac(Str(P(d, "mac")));

                    return (JsonNode?)new JsonObject
                    {
                        ["mac"] = mac,
                        ["name"] = n["name"]?.DeepClone(),
                        ["model"] = n["model"]?.DeepClone(),
                        ["status"] = n["status"]?.DeepClone(),
                        ["placeholder"] = n["placeholder"]?.DeepClone(),
                        ["clients"] = R(P(d, "num_sta")) ?? N(0),
                        ["satisfaction"] = n["satisfaction"]?.DeepClone(),
                        ["uptime"] = n["uptime"]?.DeepClone(),
                        ["radios"] = new JsonArray(radios),
                        ["history"] = new JsonArray(hist.Where(h => NormMac(Str(P(h, "ap"))) == mac).OrderBy(h => D(P(h, "time")) ?? 0).Select(h =>
                        {
                            var txPackets = D(P(h, "tx_packets"));
                            return (JsonNode?)new JsonObject
                            {
                                ["t"] = R(P(h, "time")),
                                ["clients"] = R(P(h, "num_sta")) ?? N(0),
                                ["satisfaction"] = R(P(h, "satisfaction")),
                                ["retries"] = txPackets is double tp && tp != 0 ? N((D(P(h, "tx_retries")) ?? double.NaN) / tp * 100) : null,
                                ["bytes"] = R(P(h, "bytes")) ?? N(0),
                            };
                        }).ToArray()),
                    };
                }).ToArray()),
            };
        }

        // =====================================================================
        // Client usage history
        // =====================================================================

        public async Task<JsonObject> GetClientUsageAsync(string? mac)
        {
            var m = NormMac(mac);
            var all = await NetClients();
            var c = Find(all, x => NormMac(Str(P(x, "mac"))) == m);
            var raw = c is { } found && Truthy(P(found, "mac")) ? Str(P(found, "mac"))! : string.Join(":", Regex.Matches(m, "..").Select(x => x.Value));
            var end = NowMs();
            var dailyTask = NetData("/api/s/default/stat/report/daily.user",
                new { attrs = new[] { "time", "rx_bytes", "tx_bytes" }, start = (long)(end - 30 * DayMs), end = (long)end, macs = new[] { raw } }, swallow: true);
            var hourlyTask = NetData("/api/s/default/stat/report/hourly.user",
                new { attrs = new[] { "time", "rx_bytes", "tx_bytes" }, start = (long)(end - 48 * HourMs), end = (long)end, macs = new[] { raw } }, swallow: true);
            await Task.WhenAll(dailyTask, hourlyTask);

            // UniFi reports rx/tx from the access point's side; flip so "down" means data the client downloaded.
            static JsonArray Map(List<JsonElement> rows) => new(rows.OrderBy(r => D(P(r, "time")) ?? 0).Select(r => (JsonNode?)new JsonObject
            {
                ["t"] = R(P(r, "time")),
                ["down"] = N(D(P(r, "tx_bytes")) ?? 0),
                ["up"] = N(D(P(r, "rx_bytes")) ?? 0),
            }).ToArray());

            return new JsonObject { ["daily"] = Map(dailyTask.Result), ["hourly"] = Map(hourlyTask.Result) };
        }

        public async Task<JsonObject> GetTopUsageAsync(int days)
        {
            await InitializeAsync();
            var end = NowMs();
            var rows = await Cached($"usage-{days}", TimeSpan.FromMinutes(15), () =>
                NetData("/api/s/default/stat/report/daily.user",
                    new { attrs = new[] { "time", "rx_bytes", "tx_bytes" }, start = (long)(end - days * DayMs), end = (long)end }));
            var known = await Cached("alluser", TimeSpan.FromMinutes(15), () => NetData($"/api/s/default/stat/alluser?type=all&conn=all&within={days * 24}"));
            var live = await NetClients();

            var tot = new Dictionary<string, (double Down, double Up)>();
            var order = new List<string>();
            foreach (var r in rows)
            {
                var m = NormMac(Str(P(r, "user")));
                if (!tot.TryGetValue(m, out var t)) { t = (0, 0); order.Add(m); }
                tot[m] = (t.Down + (D(P(r, "tx_bytes")) ?? 0), t.Up + (D(P(r, "rx_bytes")) ?? 0));
            }

            var clients = order.Select(mac =>
            {
                var t = tot[mac];
                var info = Find(live, c => NormMac(Str(P(c, "mac"))) == mac)
                           ?? Find(known, c => NormMac(Str(P(c, "mac"))) == mac)
                           ?? JsonSerializer.SerializeToElement(new { mac });
                var kind = ClientKind(info);
                return (Total: t.Down + t.Up, Obj: new JsonObject
                {
                    ["mac"] = mac,
                    ["name"] = FriendlyName(info, kind),
                    ["kind"] = kind,
                    ["kindLabel"] = Kinds[kind],
                    ["wired"] = Truthy(P(info, "is_wired")),
                    ["online"] = live.Any(c => NormMac(Str(P(c, "mac"))) == mac),
                    ["down"] = N(t.Down),
                    ["up"] = N(t.Up),
                    ["total"] = N(t.Down + t.Up),
                });
            }).Where(x => x.Total > 0).OrderByDescending(x => x.Total).Take(50).Select(x => (JsonNode?)x.Obj).ToArray();

            return new JsonObject { ["days"] = days, ["clients"] = new JsonArray(clients) };
        }

        // =====================================================================
        // Speed tests — starts a test on one WAN port of the gateway. UniFi runs one test at a time.
        // =====================================================================

        private sealed record SpeedtestRun(string Wan, string Ifname, double StartedAt);
        private volatile SpeedtestRun? _speedtestRun;

        private static JsonObject RunJson(SpeedtestRun r) => new() { ["wan"] = r.Wan, ["ifname"] = r.Ifname, ["startedAt"] = N(r.StartedAt) };

        public async Task<JsonObject> StartSpeedtestAsync(string? wan)
        {
            var gw = await GatewayDevice();
            var port = gw is { } g ? P(g, (wan ?? "").ToLowerInvariant()) : null;
            if (gw == null || !Truthy(P(port, "ifname"))) throw new InvalidOperationException($"Unknown WAN {wan}");
            var running = _speedtestRun;
            if (running != null && NowMs() - running.StartedAt < 120_000)
                throw new InvalidOperationException($"A speed test is already running on {running.Wan}");

            var ifname = Str(P(port, "ifname"))!;
            await Net("/api/s/default/cmd/devmgr", new { cmd = "speedtest", mac = Str(P(gw.Value, "mac")), interface_name = ifname });
            var run = new SpeedtestRun((wan ?? "").ToUpperInvariant(), ifname, NowMs());
            _speedtestRun = run;
            return RunJson(run);
        }

        public async Task<JsonObject> GetSpeedtestStatusAsync()
        {
            var run = _speedtestRun;
            if (run == null) return new JsonObject { ["running"] = false };
            var r = await Net("/api/s/default/cmd/devmgr", new { cmd = "speedtest-status" });
            var st = Arr(P(r, "data")).Select(x => (JsonElement?)x).FirstOrDefault();
            var now = NowMs();
            var finished = D(P(st, "status_summary")) == 2 && (D(P(st, "rundate")) ?? 0) * 1000 >= run.StartedAt - 5000;
            var timedOut = now - run.StartedAt > 120_000;
            var stillRunning = !finished && !timedOut;

            var output = new JsonObject
            {
                ["running"] = stillRunning,
                ["wan"] = run.Wan,
                ["elapsed"] = N(Math.Floor((now - run.StartedAt) / 1000 + 0.5)),
                ["result"] = finished ? new JsonObject
                {
                    ["down"] = R(P(st, "xput_download")),
                    ["up"] = R(P(st, "xput_upload")),
                    ["ping"] = R(P(st, "latency")),
                    ["server"] = S(P(st, "server", "provider")),
                    ["interface"] = S(P(st, "interface_name")),
                } : null,
                ["timedOut"] = timedOut && !finished,
            };
            if (!stillRunning)
            {
                _speedtestRun = null;
                // Drop cached data so the cards pick up the new result immediately.
                DropCache("speedtests", "devices");
            }
            return output;
        }

        // =====================================================================
        // Config & client aliases
        // =====================================================================

        public Task<JsonObject> GetConfigAsync()
        {
            var sites = _sites;
            var fallback = sites.Count > 0 ? Regex.Split(SiteName(sites[0]), @"\s+[-–·]\s+")[0] : "";
            var brand = _brandName != "" ? _brandName : fallback != "" ? fallback : "Network";
            // The Node "logo" feature is not ported; logo stays empty so the pages show the letter icon.
            return Task.FromResult(new JsonObject { ["brand"] = brand, ["logo"] = "" });
        }

        public async Task<JsonObject?> SetAliasAsync(string? mac, string? name, string? updatedByEmail)
        {
            await InitializeAsync();
            var m = NormMac(mac);
            if (m.Length != 12) return null;
            var alias = (name ?? "").Trim();
            if (alias.Length > 60) alias = alias[..60];
            var dbMac = string.Join(":", Regex.Matches(m, "..").Select(x => x.Value));
            var email = updatedByEmail != null && updatedByEmail.Length > 200 ? updatedByEmail[..200] : updatedByEmail;

            if (alias != "") _aliases[m] = alias;
            else _aliases.TryRemove(m, out _);
            await WithDbAsync(db => alias != "" ? db.UpsertUnifiClientAliasDbAsync(dbMac, alias, email) : db.DeleteUnifiClientAliasDbAsync(dbMac));

            return new JsonObject { ["mac"] = m, ["name"] = alias };
        }

        // =====================================================================
        // Wi-Fi SSIDs and per-SSID MAC filters
        // =====================================================================

        private static readonly Regex WlanIdRe = new("^[0-9a-f]{24}$", RegexOptions.IgnoreCase);

        /// <summary>"aabbccddeeff" / "AA-BB-..." → "aa:bb:cc:dd:ee:ff"; null when it isn't a MAC.</summary>
        private static string? ColonMac(string? mac)
        {
            var m = NormMac(mac);
            return m.Length == 12 ? string.Join(":", Regex.Matches(m, "..").Select(x => x.Value)) : null;
        }

        private static List<string> MacList(JsonElement? wlan) =>
            Arr(P(wlan, "mac_filter_list")).Select(x => ColonMac(Str(x))).Where(x => x != null).Select(x => x!).Distinct().ToList();

        private JsonObject NormaliseWlan(JsonElement w, List<JsonObject> clients, Dictionary<(string, string), (string? Name, string? RoomNo)> details, List<JsonElement> groups)
        {
            var name = Str(P(w, "name"));
            var here = clients.Where(c => ToStr(c["ssid"]) == name).ToList();
            var bands = Arr(P(w, "wlan_bands")).Select(b => Str(b)).Where(b => b != "").ToList();
            if (bands.Count == 0 && Str(P(w, "wlan_band")) is { Length: > 0 } wb) bands = wb == "both" ? new() { "2g", "5g" } : new() { wb };
            var filterOn = Truthy(P(w, "mac_filter_enabled"));
            var policy = Str(P(w, "mac_filter_policy")) == "allow" ? "allow" : "deny";
            return new JsonObject
            {
                ["id"] = Str(P(w, "_id")),
                ["name"] = name,
                ["enabled"] = Truthy(P(w, "enabled")),
                ["hidden"] = Truthy(P(w, "hide_ssid")),
                ["guest"] = Truthy(P(w, "is_guest")),
                ["security"] = Str(P(w, "security")),
                ["bands"] = new JsonArray(bands.Select(b => (JsonNode?)JsonValue.Create(b == "2g" ? "2.4 GHz" : b == "5g" ? "5 GHz" : b == "6e" ? "6 GHz" : b)).ToArray()),
                ["filterEnabled"] = filterOn,
                ["filterPolicy"] = filterOn ? policy : "off",
                ["macList"] = new JsonArray(MacList(w).Select(m => (JsonNode?)JsonValue.Create(m)).ToArray()),
                // Name / room entered in WorkNest for each MAC on the list (WN_UNIFI_SsidDevices).
                ["devices"] = new JsonArray(MacList(w).Select(m =>
                {
                    details.TryGetValue((Str(P(w, "_id")), m), out var d);
                    return (JsonNode?)new JsonObject { ["mac"] = m, ["name"] = d.Name, ["roomNo"] = d.RoomNo };
                }).ToArray()),
                ["speedLimit"] = SpeedLimitOf(Find(groups, g => Str(P(g, "_id")) == Str(P(w, "usergroup_id")))),
                ["speedProfile"] = Find(groups, g => Str(P(g, "_id")) == Str(P(w, "usergroup_id"))) is { } grp && DefaultGroup(groups) is var dg
                                   && (dg == null || Str(P(dg, "_id")) != Str(P(grp, "_id")))
                    ? new JsonObject { ["id"] = Str(P(grp, "_id")), ["name"] = Str(P(grp, "name")) } : null,
                ["clientCount"] = here.Count,
                ["clients"] = new JsonArray(here.Select(c => (JsonNode?)c.DeepClone()).ToArray()),
            };
        }

        public async Task<JsonObject> GetSsidsAsync()
        {
            await InitializeAsync();
            var wlansTask = Cached("wlans", TimeSpan.FromSeconds(20), () => NetData("/api/s/default/rest/wlanconf"));
            var groupsTask = Cached("usergroups", TimeSpan.FromSeconds(60), () => NetData("/api/s/default/rest/usergroup", swallow: true));
            var devicesTask = NetDevices();
            var clientsTask = NetClients();
            await Task.WhenAll(wlansTask, groupsTask, devicesTask, clientsTask);
            var devByMac = DevByMac(devicesTask.Result.Select(NormaliseNetDevice).ToList());
            var clients = clientsTask.Result.Where(c => !Truthy(P(c, "is_wired"))).Select(c => NormaliseClient(c, devByMac)).ToList();
            var details = new Dictionary<(string, string), (string? Name, string? RoomNo)>();
            await WithDbAsync(async db =>
            {
                foreach (var d in await db.GetUnifiSsidDevicesDbAsync()) details[(d.WlanId, d.Mac)] = (d.Name, d.RoomNo);
            });
            var wlans = wlansTask.Result.Select(w => NormaliseWlan(w, clients, details, groupsTask.Result)).ToList();
            return new JsonObject
            {
                ["ssids"] = new JsonArray(wlans.OrderByDescending(w => w["enabled"]!.GetValue<bool>()).ThenBy(w => ToStr(w["name"]), LocaleCompare).Select(w => (JsonNode?)w).ToArray()),
                ["activeCount"] = wlans.Count(w => w["enabled"]!.GetValue<bool>()),
                ["wifiClients"] = clients.Count,
            };
        }

        public async Task<JsonObject> UpdateMacFilterAsync(string wlanId, string action, string? mac, string? policy, string? reason, string? byEmail, int? byUserId, IReadOnlyList<string>? macs = null, string? deviceName = null, string? roomNo = null)
        {
            static JsonObject Error(string message) => new() { ["error"] = message };
            await InitializeAsync();
            if (!WlanIdRe.IsMatch(wlanId ?? "")) return Error("Unknown SSID.");
            action = (action ?? "").Trim().ToLowerInvariant();
            if (action is not ("add" or "remove" or "block" or "unblock" or "mode")) return Error("Unknown action.");
            var colon = ColonMac(mac);
            if (action != "mode" && colon == null) return Error("Enter a valid MAC address, e.g. aa:bb:cc:dd:ee:ff.");
            reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim() is var r && r.Length > 500 ? r[..500] : reason.Trim();

            var wlans = await NetData("/api/s/default/rest/wlanconf");
            var wlan = Find(wlans, w => Str(P(w, "_id")) == wlanId);
            if (wlan == null) return Error("This SSID no longer exists on the console.");
            var ssid = Str(P(wlan, "name"));
            var list = MacList(wlan);
            var previous = JsonSerializer.Serialize(list);
            var enabled = Truthy(P(wlan, "mac_filter_enabled"));
            var pol = Str(P(wlan, "mac_filter_policy")) == "allow" ? "allow" : "deny";

            switch (action)
            {
                case "add":
                    if (!list.Contains(colon!)) list.Add(colon!);
                    break;
                case "remove":
                    list.Remove(colon!);
                    break;
                case "block":
                    // Off → turn on as a block-list holding just this device; block-list → add; allow-list → take it out.
                    if (!enabled) { enabled = true; pol = "deny"; list = new() { colon! }; }
                    else if (pol == "deny") { if (!list.Contains(colon!)) list.Add(colon!); }
                    else list.Remove(colon!);
                    break;
                case "unblock":
                    if (!enabled) return Error("This SSID has no MAC filter, so the device is not blocked.");
                    if (pol == "deny") list.Remove(colon!);
                    else if (!list.Contains(colon!)) list.Add(colon!);
                    break;
                case "mode":
                    var p = (policy ?? "").Trim().ToLowerInvariant();
                    if (p is not ("allow" or "deny" or "off")) return Error("Mode must be allow, deny or off.");
                    // MACs added in the same step (e.g. the devices allowed when switching to an allow-list).
                    foreach (var raw in macs ?? Array.Empty<string>())
                    {
                        var m = ColonMac(raw);
                        if (m == null) return Error($"“{raw}” is not a valid MAC address (e.g. aa:bb:cc:dd:ee:ff).");
                        if (!list.Contains(m)) list.Add(m);
                    }
                    if (p == "off") enabled = false;
                    else { enabled = true; pol = p; }
                    break;
            }
            // An empty allow-list is allowed (e.g. a new SSID nobody may join yet); the screen warns before it is set.

            var wantList = list.ToHashSet();
            var answeredInTime = await PutWlanAndConfirmAsync(wlanId,
                new { mac_filter_enabled = enabled, mac_filter_policy = pol, mac_filter_list = list },
                w => Truthy(P(w, "mac_filter_enabled")) == enabled
                     && (!enabled || (Str(P(w, "mac_filter_policy")) == "allow" ? "allow" : "deny") == pol)
                     && MacList(w).ToHashSet().SetEquals(wantList));

            // A device that just lost access is disconnected now (otherwise it stays on until it reconnects).
            var cutOff = colon != null && enabled && (pol == "deny" ? list.Contains(colon) : !list.Contains(colon));
            if (cutOff && action != "mode")
            {
                var live = Find(await NetClients(), c => ColonMac(Str(P(c, "mac"))) == colon && Str(P(c, "essid")) == ssid);
                if (live != null)
                {
                    try { await Net("/api/s/default/cmd/stamgr", new { cmd = "kick-sta", mac = colon }); }
                    catch (Exception ex) { _logger.LogWarning("UniFi kick-sta {Mac} failed: {Error}", colon, ex.Message); }
                }
            }

            // Name / room of the device: saved when it is added (or blocked), dropped when it leaves the list.
            static string? Clip(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : v.Trim().Length > max ? v.Trim()[..max] : v.Trim();
            var name = Clip(deviceName, 100);
            var room = Clip(roomNo, 50);
            if (colon != null && list.Contains(colon) && (action is "add" or "block" or "unblock") && (name != null || room != null))
                await WithDbAsync(db => db.UpsertUnifiSsidDeviceDbAsync(wlanId, colon, name, room, byEmail));
            else if (colon != null && !list.Contains(colon))
                await WithDbAsync(db => db.DeleteUnifiSsidDeviceDbAsync(wlanId, colon));

            var logPolicy = enabled ? pol : "off";
            await WithDbAsync(db => db.InsertUnifiMacFilterLogDbAsync(wlanId, ssid, colon, action, logPolicy, reason,
                byEmail is { Length: > 200 } ? byEmail[..200] : byEmail, byUserId, previous));
            DropCache("wlans", "clients");
            return WithConfirmation((await GetSsidsAsync())["ssids"]!.AsArray().FirstOrDefault(w => ToStr(w!["id"]) == wlanId)?.DeepClone().AsObject()
                   ?? new JsonObject { ["id"] = wlanId }, answeredInTime);
        }

        /// <summary>
        /// Sends a change to one SSID. If UniFi times out, the change may still have been saved (it often is), so the
        /// SSID is read back up to three times (after 3, 6 and 10 s) and <paramref name="isApplied"/> decides.
        /// Returns true when UniFi answered normally, false when it timed out but the change is there; throws a
        /// friendly "not applied" error when it never shows up.
        /// </summary>
        private async Task<bool> PutWlanAndConfirmAsync(string wlanId, object body, Func<JsonElement, bool> isApplied)
        {
            var consoleId = ConsoleId() ?? throw new InvalidOperationException("No UniFi console found for this API key");
            try
            {
                await _client.NetworkAsync(consoleId, $"/api/s/default/rest/wlanconf/{wlanId}", body, HttpMethod.Put);
                return true;
            }
            catch (UnifiTimeoutException)
            {
                _logger.LogWarning("UniFi timed out changing WLAN {WlanId}; checking whether the change was saved", wlanId);
                foreach (var wait in new[] { 3, 6, 10 })
                {
                    await Task.Delay(TimeSpan.FromSeconds(wait));
                    try
                    {
                        var wlan = Find(await NetData("/api/s/default/rest/wlanconf"), w => Str(P(w, "_id")) == wlanId);
                        if (wlan != null && isApplied(wlan.Value))
                        {
                            _logger.LogInformation("UniFi change to WLAN {WlanId} was saved despite the timeout", wlanId);
                            return false;
                        }
                    }
                    catch (Exception ex) { _logger.LogWarning("Re-reading WLAN {WlanId} failed: {Error}", wlanId, ex.Message); }
                }
                throw new InvalidOperationException("Not applied: UniFi did not save this change. Please try again in a minute.");
            }
        }

        /// <summary>Marks a returned SSID as confirmed only by reading it back after a UniFi timeout.</summary>
        private static JsonObject WithConfirmation(JsonObject ssid, bool answeredInTime)
        {
            if (!answeredInTime) ssid["confirmedAfterTimeout"] = true;
            return ssid;
        }

        public async Task<JsonObject> SetSsidHiddenAsync(string wlanId, bool hidden, string? reason, string? byEmail, int? byUserId)
        {
            await InitializeAsync();
            if (!WlanIdRe.IsMatch(wlanId ?? "")) return new JsonObject { ["error"] = "Unknown SSID." };
            reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim() is var r && r.Length > 500 ? r[..500] : reason.Trim();
            var wlan = Find(await Cached("wlans", TimeSpan.FromSeconds(20), () => NetData("/api/s/default/rest/wlanconf")), w => Str(P(w, "_id")) == wlanId);
            if (wlan == null) return new JsonObject { ["error"] = "This SSID no longer exists on the console." };
            var ssid = Str(P(wlan, "name"));

            var answeredInTime = await PutWlanAndConfirmAsync(wlanId, new { hide_ssid = hidden }, w => Truthy(P(w, "hide_ssid")) == hidden);

            var filterOn = Truthy(P(wlan, "mac_filter_enabled"));
            var policy = filterOn ? (Str(P(wlan, "mac_filter_policy")) == "allow" ? "allow" : "deny") : "off";
            await WithDbAsync(db => db.InsertUnifiMacFilterLogDbAsync(wlanId, ssid, null, hidden ? "hide" : "unhide", policy, reason,
                byEmail is { Length: > 200 } ? byEmail[..200] : byEmail, byUserId, null));
            DropCache("wlans");
            return WithConfirmation((await GetSsidsAsync())["ssids"]!.AsArray().FirstOrDefault(w => ToStr(w!["id"]) == wlanId)?.DeepClone().AsObject()
                   ?? new JsonObject { ["id"] = wlanId }, answeredInTime);
        }

        /// <summary>{ downMbps, upMbps } of a UniFi user group (kbps, -1 = unlimited); null when it has no limit.</summary>
        private static JsonObject? SpeedLimitOf(JsonElement? group)
        {
            static int? Mbps(JsonElement? v) => D(v) is double k && k > 0 ? (int)Math.Round(k / 1000) : null;
            var down = Mbps(P(group, "qos_rate_max_down"));
            var up = Mbps(P(group, "qos_rate_max_up"));
            return down == null && up == null ? null : new JsonObject { ["downMbps"] = down, ["upMbps"] = up };
        }

        /// <summary>The "Default" user group (no limit) every SSID uses unless a speed profile is chosen.</summary>
        private static JsonElement? DefaultGroup(List<JsonElement> groups) =>
            Find(groups, g => Truthy(P(g, "attr_no_delete")) || string.Equals(Str(P(g, "name")), "Default", StringComparison.OrdinalIgnoreCase));

        public async Task<JsonObject> GetSpeedProfilesAsync()
        {
            await InitializeAsync();
            var groupsTask = NetData("/api/s/default/rest/usergroup");
            var wlansTask = Cached("wlans", TimeSpan.FromSeconds(20), () => NetData("/api/s/default/rest/wlanconf"));
            await Task.WhenAll(groupsTask, wlansTask);
            var def = DefaultGroup(groupsTask.Result);
            var defId = def != null ? Str(P(def, "_id")) : "";
            return new JsonObject
            {
                ["profiles"] = new JsonArray(groupsTask.Result
                    .Where(g => Str(P(g, "_id")) != defId)
                    .OrderBy(g => Str(P(g, "name")), LocaleCompare)
                    .Select(g =>
                    {
                        var id = Str(P(g, "_id"));
                        var limit = SpeedLimitOf(g);
                        return (JsonNode?)new JsonObject
                        {
                            ["id"] = id,
                            ["name"] = Str(P(g, "name")),
                            ["downMbps"] = limit?["downMbps"]?.DeepClone(),
                            ["upMbps"] = limit?["upMbps"]?.DeepClone(),
                            ["ssids"] = new JsonArray(wlansTask.Result.Where(w => Str(P(w, "usergroup_id")) == id)
                                .Select(w => (JsonNode?)JsonValue.Create(Str(P(w, "name")))).ToArray()),
                        };
                    }).ToArray()),
            };
        }

        public async Task<JsonObject> CreateSpeedProfileAsync(string? name, int? downMbps, int? upMbps)
        {
            static JsonObject Error(string message) => new() { ["error"] = message };
            await InitializeAsync();
            name = (name ?? "").Trim();
            if (name.Length is < 1 or > 64) return Error("Give the profile a name (up to 64 characters).");
            if (downMbps == null && upMbps == null) return Error("Enter a download and/or upload speed.");
            if (downMbps is < 1 or > 10000 || upMbps is < 1 or > 10000) return Error("Speed must be between 1 and 10,000 Mbps.");
            var groups = await NetData("/api/s/default/rest/usergroup");
            if (Find(groups, g => string.Equals(Str(P(g, "name")), name, StringComparison.OrdinalIgnoreCase)) != null)
                return Error("A profile with this name already exists.");
            JsonElement? g0;
            var answeredInTime = true;
            try
            {
                var created = await _client.NetworkAsync(ConsoleId() ?? throw new InvalidOperationException("No UniFi console found for this API key"),
                    "/api/s/default/rest/usergroup",
                    new { name, qos_rate_max_down = downMbps.HasValue ? downMbps.Value * 1000 : -1, qos_rate_max_up = upMbps.HasValue ? upMbps.Value * 1000 : -1 },
                    HttpMethod.Post);
                g0 = Arr(P(created, "data")).FirstOrDefault();
            }
            catch (UnifiTimeoutException)
            {
                // Timed out: the profile may exist anyway, so look for it by name before reporting a failure.
                answeredInTime = false;
                g0 = null;
                foreach (var wait in new[] { 3, 6, 10 })
                {
                    await Task.Delay(TimeSpan.FromSeconds(wait));
                    try { g0 = Find(await NetData("/api/s/default/rest/usergroup"), g => string.Equals(Str(P(g, "name")), name, StringComparison.OrdinalIgnoreCase)); }
                    catch (Exception ex) { _logger.LogWarning("Re-reading speed profiles failed: {Error}", ex.Message); }
                    if (g0 != null) break;
                }
                if (g0 == null) return Error("Not applied: UniFi did not create the profile. Please try again in a minute.");
            }
            if (Str(P(g0, "_id")) == "") return Error("UniFi did not create the profile.");
            DropCache("usergroups");
            var profile = new JsonObject { ["id"] = Str(P(g0, "_id")), ["name"] = name, ["downMbps"] = downMbps, ["upMbps"] = upMbps, ["ssids"] = new JsonArray() };
            return WithConfirmation(profile, answeredInTime);
        }

        public async Task<JsonObject> SetSsidSpeedProfileAsync(string wlanId, string? profileId, string? reason, string? byEmail, int? byUserId)
        {
            static JsonObject Error(string message) => new() { ["error"] = message };
            await InitializeAsync();
            if (!WlanIdRe.IsMatch(wlanId ?? "")) return Error("Unknown SSID.");
            reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim() is var r && r.Length > 400 ? r[..400] : reason.Trim();

            // The SSID list shown on the page (cached 20 s) is enough here: only its name and current profile are needed.
            var wlan = Find(await Cached("wlans", TimeSpan.FromSeconds(20), () => NetData("/api/s/default/rest/wlanconf")), w => Str(P(w, "_id")) == wlanId);
            if (wlan == null) return Error("This SSID no longer exists on the console.");
            var ssid = Str(P(wlan, "name"));

            var groups = await NetData("/api/s/default/rest/usergroup");
            // No profile = the Default group (no limit).
            var group = string.IsNullOrWhiteSpace(profileId) ? DefaultGroup(groups) : Find(groups, g => Str(P(g, "_id")) == profileId);
            if (group == null) return Error(string.IsNullOrWhiteSpace(profileId) ? "UniFi has no Default profile to remove the limit with." : "This speed profile no longer exists on the console.");
            var groupId = Str(P(group, "_id"));

            var answeredInTime = Str(P(wlan, "usergroup_id")) == groupId
                || await PutWlanAndConfirmAsync(wlanId, new { usergroup_id = groupId }, w => Str(P(w, "usergroup_id")) == groupId);

            var limit = string.IsNullOrWhiteSpace(profileId) ? null : SpeedLimitOf(group);
            var limitText = limit == null ? "speed: no limit"
                : $"speed profile {Str(P(group, "name"))}: download {limit["downMbps"]?.ToString() ?? "no limit"} / upload {limit["upMbps"]?.ToString() ?? "no limit"} Mbps per device";
            var filterOn = Truthy(P(wlan, "mac_filter_enabled"));
            var policy = filterOn ? (Str(P(wlan, "mac_filter_policy")) == "allow" ? "allow" : "deny") : "off";
            await WithDbAsync(db => db.InsertUnifiMacFilterLogDbAsync(wlanId, ssid, null, "speed", policy,
                reason == null ? limitText : $"{limitText} · {reason}",
                byEmail is { Length: > 200 } ? byEmail[..200] : byEmail, byUserId, null));
            DropCache("wlans", "usergroups");
            return WithConfirmation((await GetSsidsAsync())["ssids"]!.AsArray().FirstOrDefault(w => ToStr(w!["id"]) == wlanId)?.DeepClone().AsObject()
                   ?? new JsonObject { ["id"] = wlanId }, answeredInTime);
        }

        // ---- which WorkNest location the UniFi network belongs to ----
        private const string LocationIdsKey = "LocationIds";
        private volatile int[]? _locationIds;
        private DateTime _locationIdsAt;

        public async Task<IReadOnlyList<int>> GetLocationIdsAsync()
        {
            var cached = _locationIds;
            if (cached != null && DateTime.UtcNow - _locationIdsAt < TimeSpan.FromSeconds(60)) return cached;
            string? raw = null;
            await WithDbAsync(async db => raw = await db.GetUnifiSettingDbAsync(LocationIdsKey));
            var ids = (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var id) ? id : 0).Where(id => id > 0).Distinct().ToArray();
            _locationIds = ids;
            _locationIdsAt = DateTime.UtcNow;
            return ids;
        }

        public async Task<bool> SetLocationIdsAsync(IReadOnlyList<int> locationIds, string? byEmail)
        {
            var ids = locationIds.Where(id => id > 0).Distinct().ToArray();
            var ok = await WithDbAsync(db => db.SetUnifiSettingDbAsync(LocationIdsKey, string.Join(",", ids), byEmail));
            if (ok) { _locationIds = ids; _locationIdsAt = DateTime.UtcNow; }
            return ok;
        }

        public async Task<JsonObject> GetMacFilterLogAsync(string? wlanId, string? mac)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDbRepository>();
            var rows = await db.GetUnifiMacFilterLogDbAsync(wlanId != null && WlanIdRe.IsMatch(wlanId) ? wlanId : null, ColonMac(mac), 200);
            return new JsonObject
            {
                ["entries"] = new JsonArray(rows.Select(r => (JsonNode?)new JsonObject
                {
                    ["id"] = Convert.ToInt32(r["Id"]),
                    ["at"] = r["LoggedAt"] is DateTime d ? Iso(DateTime.SpecifyKind(d, DateTimeKind.Utc)) : null,
                    ["wlanId"] = Convert.ToString(r["WlanId"]),
                    ["ssid"] = Convert.ToString(r["Ssid"]),
                    ["mac"] = Convert.ToString(r["Mac"]),
                    ["action"] = Convert.ToString(r["Action"]),
                    ["policy"] = Convert.ToString(r["Policy"]),
                    ["reason"] = Convert.ToString(r["Reason"]),
                    ["by"] = Convert.ToString(r["ByName"]),
                }).ToArray()),
            };
        }

        // =====================================================================
        // JSON helpers (JavaScript semantics: `?.`, `??`, `||`, truthiness)
        // =====================================================================

        private static readonly StringComparer LocaleCompare = StringComparer.Create(CultureInfo.InvariantCulture, false);

        private static Dictionary<string, JsonObject> DevByMac(List<JsonObject> devs)
        {
            var map = new Dictionary<string, JsonObject>();
            foreach (var d in devs) map[ToStr(d["mac"])] = d;
            return map;
        }

        /// <summary>Property of an object; null when missing, JSON null, or the parent isn't an object.</summary>
        private static JsonElement? P(JsonElement? e, string name)
        {
            if (e is not { ValueKind: JsonValueKind.Object } o) return null;
            return o.TryGetProperty(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? v : null;
        }

        private static JsonElement? P(JsonElement? e, string a, string b) => P(P(e, a), b);

        private static JsonElement? Find(IEnumerable<JsonElement> list, Func<JsonElement, bool> predicate)
        {
            foreach (var x in list) if (predicate(x)) return x;
            return null;
        }

        private static IEnumerable<JsonElement> Arr(JsonElement? e) =>
            e is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : Enumerable.Empty<JsonElement>();

        private static bool Truthy(JsonElement? e) => e switch
        {
            null => false,
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.Number } n => n.GetDouble() != 0,
            { ValueKind: JsonValueKind.String } s => s.GetString()!.Length > 0,
            { ValueKind: JsonValueKind.Object or JsonValueKind.Array } => true,
            _ => false,
        };

        private static bool IsFalse(JsonElement? e) => e is { ValueKind: JsonValueKind.False };

        /// <summary>`String(v)` for present values; null when missing.</summary>
        private static string? Str(JsonElement? e) => e switch
        {
            null => null,
            { ValueKind: JsonValueKind.String } s => s.GetString(),
            { ValueKind: JsonValueKind.True } => "true",
            { ValueKind: JsonValueKind.False } => "false",
            { } x => x.GetRawText(),
        };

        /// <summary>`a || b || ''` — the first truthy value as a string.</summary>
        private static string S(params JsonElement?[] values)
        {
            foreach (var v in values) if (Truthy(v)) return Str(v)!;
            return "";
        }

        private static double? D(JsonElement? e) => e is { ValueKind: JsonValueKind.Number } n ? n.GetDouble() : null;

        // num(): finite numbers only, otherwise 0.
        private static double Num(JsonElement? e) => D(e) is double d && double.IsFinite(d) ? d : 0;

        // numOrNull(): Number(v), or null for missing / '' / NaN.
        private static double? NumOrNull(JsonElement? e)
        {
            switch (e)
            {
                case null: return null;
                case { ValueKind: JsonValueKind.Number } n: return n.GetDouble();
                case { ValueKind: JsonValueKind.True }: return 1;
                case { ValueKind: JsonValueKind.False }: return 0;
                case { ValueKind: JsonValueKind.String } s:
                    var str = s.GetString()!;
                    if (str == "") return null;
                    if (str.Trim() == "") return 0;
                    return double.TryParse(str.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
                default: return null;
            }
        }

        /// <summary>Raw pass-through of a source value (`x ?? null`).</summary>
        private static JsonNode? R(JsonElement? e) => e is { } x ? JsonSerializer.SerializeToNode(x) : null;

        /// <summary>Number output: integers without a fraction, NaN/Infinity as null (like JSON.stringify).</summary>
        private static JsonNode? N(double? d)
        {
            if (d is not double v || !double.IsFinite(v)) return null;
            return v == Math.Floor(v) && Math.Abs(v) < 9e15 ? JsonValue.Create((long)v) : JsonValue.Create(v);
        }

        private static double? ToNum(JsonNode? n)
        {
            if (n is not JsonValue v) return null;
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<JsonElement>(out var e) && e.ValueKind == JsonValueKind.Number) return e.GetDouble();
            return null;
        }

        private static string ToStr(JsonNode? n)
        {
            if (n == null) return "";
            if (n is JsonValue v && v.TryGetValue<string>(out var s)) return s;
            return n.ToJsonString();
        }

        private static bool ToBool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;

        // toFixed(1): halves round away from zero.
        private static string Fixed1(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero).ToString("F1", CultureInfo.InvariantCulture);

        private static string NormMac(string? mac) => Regex.Replace((mac ?? "").ToLowerInvariant(), "[^0-9a-f]", "");

        private static string Iso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        private static double NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static DateTimeOffset ParseDate(string? s) =>
            DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : DateTimeOffset.MinValue;
    }
}
