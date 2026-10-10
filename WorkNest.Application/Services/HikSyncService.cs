using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>Settings for <see cref="HikSyncService"/> ("HikSync" section).</summary>
    public class HikSyncOptions
    {
        /// <summary>Master switch for the background schedule (the endpoints work either way).</summary>
        public bool Enabled { get; set; }
        /// <summary>
        /// false (default): the online check probes machines that were online plus ONE rotating offline machine per
        /// pass. Rapid connects to many dead forwarded ports look like a port scan to the office router, which then
        /// blackholes the source (it happened on 2026-09-10). Set true only once the router allows full sweeps.
        /// </summary>
        public bool FullOnlineSweep { get; set; }
        /// <summary>
        /// Gentle mode: offline machines re-tested per pass (one after another, a short pause apart, so the router
        /// doesn't see a burst). With the 2-minute pass, 3 means every offline machine is retried every few minutes.
        /// </summary>
        public int OfflineProbesPerPass { get; set; } = 3;
        /// <summary>How long the online check waits for a machine (same reach as the Test button needs on slow links).</summary>
        public int PingTimeoutSeconds { get; set; } = 6;
        /// <summary>Machine time zone, minutes east of UTC (Pakistan = 300).</summary>
        public int DeviceTzOffsetMinutes { get; set; } = 300;
    }

    /// <summary>
    /// Port of HIK_Access-Control's scheduler.js / sync.js / machineCache.js: keeps the WN_HIK_* tables in step
    /// with the machines. Same rules, limits and ordering as the Node jobs; ISAPI traffic goes through
    /// <see cref="IHikIsapiClient"/> (2 connections per public IP, 6 fleet-wide, Host2 failover).
    /// Not carried over: pushing a face from a file path on the Node server's disk (vaulted face templates are used).
    /// Singleton: holds the in-memory roster/card caches and job status.
    /// </summary>
    public class HikSyncService : IHikSyncService, IHikRosterCache
    {
        public const string JobOnline = "online";
        public const string JobMaintenance = "maintenance";
        public const string JobWatch = "watch";
        public const string JobClock = "clock";
        public const string JobEvents = "events";
        public const string JobSnapshots = "snapshots";
        public const string JobUsers = "users";
        public const string JobCards = "cards";
        public const string JobFaceVault = "facevault";
        public const string JobRenewals = "renewals";
        public const string JobExpiry = "expiry";
        public const string JobGrants = "grants";
        public const string JobCredentials = "credentials";
        public const string JobQueue = "queue";

        private static readonly string[] Jobs =
        {
            JobOnline, JobMaintenance, JobWatch, JobClock, JobEvents, JobSnapshots, JobUsers, JobCards,
            JobFaceVault, JobRenewals, JobExpiry, JobGrants, JobCredentials, JobQueue
        };

        // Same values as machineCache.js
        private static readonly TimeSpan RosterTtl = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan RosterFailTtl = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan CardsTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan SnapshotStale = TimeSpan.FromMinutes(10);

        // Hikvision access_event_category codes (HIK eventCategories.js).
        private static readonly Dictionary<int, string> EventLabels = new()
        {
            [1] = "Entry authorized", [2] = "Card + password", [21] = "Door opened", [22] = "Door closed",
            [23] = "Door open timeout", [27] = "Remote unlock (dashboard)", [38] = "Fingerprint OK", [39] = "Fingerprint denied",
            [75] = "Face OK", [76] = "Face not recognized", [112] = "Entry denied (expired)", [8] = "Card verify failed",
            [9] = "Unregistered card", [24] = "Door forced open (alarm)", [104] = "Face OK", [151] = "Machine event 151"
        };

        private readonly IHikSyncRepository _repo;
        private readonly IHikIsapiClient _isapi;
        private readonly IBusinessClock _clock;
        private readonly ILogger<HikSyncService> _logger;
        private readonly HikSyncOptions _options;

        private readonly ConcurrentDictionary<string, HikSyncJobStatus> _status = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _jobGates = new();
        private readonly ConcurrentDictionary<int, RosterEntry> _rosters = new();
        private readonly ConcurrentDictionary<int, CardEntry> _cards = new();
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _scanGates = new();
        private int _highWater;
        private string? _rosterSignature;

        private sealed class RosterEntry
        {
            public DateTime At;
            public List<JsonObject>? Data;
            public DateTime? FailedAt;
            public string? LastError;
        }

        private sealed class CardEntry
        {
            public DateTime At;
            public List<CardHolder>? Data;
        }

        private sealed record CardHolder(string CardNo, string EmployeeNo);

        private sealed class HikUnreachableException : Exception
        {
            public HikUnreachableException(string message) : base(message) { }
        }

        public HikSyncService(IHikSyncRepository repo, IHikIsapiClient isapi, IBusinessClock clock, ILogger<HikSyncService> logger, HikSyncOptions options)
        {
            _repo = repo;
            _isapi = isapi;
            _clock = clock;
            _logger = logger;
            _options = options;
            foreach (var j in Jobs) _status[j] = new HikSyncJobStatus { Job = j };
        }

        public IReadOnlyList<string> JobNames => Jobs;

        public IReadOnlyList<HikSyncJobStatus> GetStatus() => Jobs.Select(j => _status[j]).ToList();

        // ---- job runner ------------------------------------------------------------------------

        public async Task<HikSyncJobStatus> RunJobAsync(string job)
        {
            job = (job ?? string.Empty).Trim().ToLowerInvariant();
            if (!Jobs.Contains(job)) throw new ArgumentException($"Unknown HIK sync job '{job}'. Jobs: {string.Join(", ", Jobs)}.");

            var gate = _jobGates.GetOrAdd(job, _ => new SemaphoreSlim(1, 1));
            var status = _status[job];
            if (!await gate.WaitAsync(0)) return status; // already running — same as HIK's busy guards

            status.Running = true;
            status.LastStartedAt = _clock.Now;
            try
            {
                var result = job switch
                {
                    JobOnline => await RunOnlineCheckAsync(),
                    JobMaintenance => await RunMaintenanceAsync(),
                    JobWatch => await RunRosterWatchAsync(),
                    JobClock => await RunClockSyncAsync(),
                    JobEvents => await ArchiveEventsAsync(),
                    JobSnapshots => await RefreshSnapshotsAsync(8),
                    JobUsers => await SyncUsersTableAsync(),
                    JobCards => await SyncCardGrantsAsync(),
                    JobFaceVault => await SweepFaceVaultAsync(),
                    JobRenewals => await MigrateRenewedBookingsAsync(),
                    JobExpiry => await RunExpiryPassAsync(),
                    JobGrants => await SyncAllPendingAsync(),
                    JobCredentials => await RunCredentialSyncAsync(),
                    JobQueue => await ReplayPendingOpsAsync(),
                    _ => "unknown job"
                };
                status.LastOk = true;
                status.LastResult = result;
            }
            catch (Exception ex)
            {
                status.LastOk = false;
                status.LastResult = ex.Message;
                _logger.LogError(ex, "HIK sync job {Job} failed", job);
            }
            finally
            {
                status.Running = false;
                status.LastFinishedAt = _clock.Now;
                gate.Release();
            }
            return status;
        }

        /// <summary>The 5-minute bundle (HIK startScheduler): each step independent, one failure doesn't stop the rest.</summary>
        private async Task<string> RunMaintenanceAsync()
        {
            var parts = new List<string>();
            async Task Step(string name, Func<Task<string>> step)
            {
                try { parts.Add($"{name}: {await step()}"); }
                catch (Exception ex) { parts.Add($"{name}: failed ({ex.Message})"); _logger.LogError(ex, "HIK maintenance step {Step} failed", name); }
            }
            await Step(JobEvents, ArchiveEventsAsync);
            await Step(JobFaceVault, SweepFaceVaultAsync);
            await Step(JobUsers, SyncUsersTableAsync);
            await Step(JobCards, SyncCardGrantsAsync);
            await Step(JobSnapshots, () => RefreshSnapshotsAsync(8));
            await Step(JobRenewals, MigrateRenewedBookingsAsync);
            await Step(JobExpiry, RunExpiryPassAsync);
            await Step(JobGrants, SyncAllPendingAsync);
            await Step(JobCredentials, RunCredentialSyncAsync);
            return string.Join("; ", parts);
        }

        // ---- helpers ---------------------------------------------------------------------------

        private string NowLocalIso() => _clock.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        private static string PersonKey(string? employeeNo, string? name) => $"{employeeNo}||{(name ?? string.Empty).Trim().ToLowerInvariant()}";

        private static string Emp(JsonObject u) => u["employeeNo"]?.ToString() ?? string.Empty;
        private static string NameOf(JsonObject u) => (u["name"]?.ToString() ?? string.Empty).Trim();
        private static int Num(JsonObject u, string key) => int.TryParse(u[key]?.ToString(), out var n) ? n : 0;

        private static string? S(IDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v != null ? v.ToString() : null;
        private static int I(IDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v != null && int.TryParse(v.ToString(), out var n) ? n : 0;

        private static bool IsUnreachable(Exception ex) =>
            ex is HikUnreachableException ||
            Regex.IsMatch(ex.Message, "timed out|timeout|ENETUNREACH|ECONNREFUSED|ETIMEDOUT|EHOSTUNREACH|unreachable", RegexOptions.IgnoreCase);

        private static void ThrowIfFailed(HikIsapiResult r, string fallback)
        {
            if (r.Ok) return;
            if (r.Unreachable) throw new HikUnreachableException(r.Error ?? fallback);
            throw new InvalidOperationException(r.Error ?? fallback);
        }

        /// <summary>logSync: never lets a log failure break a job (HIK fires it and forgets).</summary>
        private async Task Log(int? employeeId, int? deviceId, string action, bool ok, object? detail)
        {
            try
            {
                var text = detail switch { null => null, string s => s, _ => JsonSerializer.Serialize(detail) };
                await _repo.LogAsync(employeeId, deviceId, action, ok, text);
            }
            catch (Exception ex) { _logger.LogWarning("HIK sync log write failed: {Error}", ex.Message); }
        }

        private async Task SetSetting(string key, string value)
        {
            try { await _repo.SetSettingAsync(key, value); } catch { /* best effort */ }
        }

        // ---- machine roster / card caches (machineCache.js) ------------------------------------

        private static bool FreshEnough(DateTime? at, DateTime now, TimeSpan ttl) => at.HasValue && now - at.Value < ttl;

        private async Task<List<JsonObject>> ScanRosterAsync(HikSyncDevice dev)
        {
            var users = new List<JsonObject>();
            var pos = 0;
            for (var i = 0; i < 100; i++)
            {
                var page = await _isapi.SearchPersonsAsync(dev, pos, 60, TimeSpan.FromSeconds(2.2));
                if (!page.Ok) throw new HikUnreachableException($"{dev.Name}: user search failed ({page.Error})");
                users.AddRange(page.List);
                if (page.List.Count == 0 || users.Count >= page.Total) break;
                pos += page.List.Count;
            }
            return users;
        }

        private static List<JsonObject>? ParseRoster(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonNode.Parse(json) is JsonArray arr ? arr.OfType<JsonObject>().Select(o => (JsonObject)o.DeepClone()).ToList() : null; }
            catch (JsonException) { return null; }
        }

        private static List<CardHolder>? ParseCards(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonNode.Parse(json) is JsonArray arr
                    ? arr.OfType<JsonObject>().Select(c => new CardHolder(c["cardNo"]?.ToString() ?? "", c["employeeNo"]?.ToString() ?? "")).ToList()
                    : null;
            }
            catch (JsonException) { return null; }
        }

        private async Task TrackHighWaterAsync(List<JsonObject> users)
        {
            var mx = users.Select(u => int.TryParse(Emp(u), out var n) ? n : 0).Where(n => n < 9000).DefaultIfEmpty(0).Max();
            if (mx > _highWater)
            {
                _highWater = mx;
                await SetSetting("max_member_no", mx.ToString());
            }
        }

        /// <summary>Full roster of a machine: memory → DB snapshot → live scan. Throws when unreachable.</summary>
        private async Task<List<JsonObject>> GetRosterAsync(HikSyncDevice dev, TimeSpan maxAge)
        {
            var now = _clock.Now;
            _rosters.TryGetValue(dev.Id, out var e);

            if (!dev.Online)
            {
                if (e?.Data != null) return e.Data;
                // last known snapshot (any age) beats nothing for a dead machine
                var snap = await _repo.GetSnapshotAsync(dev.Id);
                var users = ParseRoster(snap?.Users);
                if (users != null) { _rosters[dev.Id] = new RosterEntry { At = now, Data = users }; return users; }
                throw new HikUnreachableException("machine offline");
            }

            if (e?.Data != null && now - e.At < maxAge) return e.Data;
            if (e?.FailedAt != null && now - e.FailedAt.Value < RosterFailTtl && !(e.Data != null && maxAge > RosterTtl))
                throw new HikUnreachableException(e.LastError ?? "machine unreachable (cached)");

            // One scan per machine at a time; concurrent callers share its result.
            var gate = _scanGates.GetOrAdd(dev.Id, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (_rosters.TryGetValue(dev.Id, out var again) && again.Data != null && _clock.Now - again.At < maxAge) return again.Data;

                var snap = await _repo.GetSnapshotAsync(dev.Id);
                if (FreshEnough(snap?.UsersAt, _clock.Now, maxAge))
                {
                    var cached = ParseRoster(snap!.Users);
                    if (cached != null) { _rosters[dev.Id] = new RosterEntry { At = _clock.Now, Data = cached }; return cached; }
                }

                var users = await ScanRosterAsync(dev);
                _rosters[dev.Id] = new RosterEntry { At = _clock.Now, Data = users };
                await TrackHighWaterAsync(users);
                try { await _repo.SaveSnapshotAsync(dev.Id, cards: false, new JsonArray(users.Select(u => (JsonNode)u.DeepClone()).ToArray()).ToJsonString()); }
                catch (Exception ex) { _logger.LogWarning("HIK roster snapshot save failed for {Device}: {Error}", dev.Name, ex.Message); }
                return users;
            }
            catch (Exception ex)
            {
                // keep stale data, remember the failure briefly
                _rosters[dev.Id] = new RosterEntry { At = e?.At ?? DateTime.MinValue, Data = e?.Data, FailedAt = _clock.Now, LastError = ex.Message };
                throw;
            }
            finally { gate.Release(); }
        }

        /// <summary>Full card table of a machine ([{cardNo, employeeNo}]): memory → DB snapshot → live scan.</summary>
        private async Task<List<CardHolder>> GetCardTableAsync(HikSyncDevice dev, TimeSpan maxAge)
        {
            if (_cards.TryGetValue(dev.Id, out var e) && e.Data != null && _clock.Now - e.At < maxAge) return e.Data;

            var snap = await _repo.GetSnapshotAsync(dev.Id);
            if (snap?.Cards != null && (FreshEnough(snap.CardsAt, _clock.Now, maxAge) || !dev.Online))
            {
                var cached = ParseCards(snap.Cards);
                if (cached != null) { _cards[dev.Id] = new CardEntry { At = _clock.Now, Data = cached }; return cached; }
            }
            if (!dev.Online) throw new HikUnreachableException("machine offline");

            var all = new List<CardHolder>();
            var pos = 0;
            for (var i = 0; i < 60; i++)
            {
                var page = await _isapi.ReadAllCardsAsync(dev, pos, 100, TimeSpan.FromSeconds(2.5));
                if (!page.Ok) { _cards.TryRemove(dev.Id, out _); throw new HikUnreachableException($"{dev.Name}: card search failed ({page.Error})"); }
                all.AddRange(page.List.Select(c => new CardHolder(c["cardNo"]?.ToString() ?? "", c["employeeNo"]?.ToString() ?? "")));
                if (page.List.Count == 0 || all.Count >= page.Total) break;
                pos += page.List.Count;
            }
            _cards[dev.Id] = new CardEntry { At = _clock.Now, Data = all };
            var json = JsonSerializer.Serialize(all.Select(c => new { cardNo = c.CardNo, employeeNo = c.EmployeeNo }));
            try { await _repo.SaveSnapshotAsync(dev.Id, cards: true, json); }
            catch (Exception ex) { _logger.LogWarning("HIK card snapshot save failed for {Device}: {Error}", dev.Name, ex.Message); }
            return all;
        }

        /// <summary>After anything that changes users on a machine, so the next read reflects it.</summary>
        private async Task InvalidateRosterAsync(int? deviceId)
        {
            if (deviceId == null) { _rosters.Clear(); _cards.Clear(); }
            else { _rosters.TryRemove(deviceId.Value, out _); _cards.TryRemove(deviceId.Value, out _); }
            try { await _repo.InvalidateSnapshotsAsync(deviceId); } catch { /* next pass */ }
        }

        // ---- IHikRosterCache (used by the machine endpoints) -------------------------------------

        async Task<IReadOnlyList<JsonObject>> IHikRosterCache.GetRosterAsync(HikSyncDevice device, TimeSpan? maxAge) =>
            await GetRosterAsync(device, maxAge ?? RosterTtl);

        async Task<IReadOnlyList<(string CardNo, string EmployeeNo)>> IHikRosterCache.GetCardTableAsync(HikSyncDevice device, TimeSpan? maxAge) =>
            (await GetCardTableAsync(device, maxAge ?? CardsTtl)).Select(c => (c.CardNo, c.EmployeeNo)).ToList();

        Task IHikRosterCache.InvalidateAsync(int? deviceId) => InvalidateRosterAsync(deviceId);

        // ---- online check (runOnlineCheck) -----------------------------------------------------

        private async Task<string> RunOnlineCheckAsync()
        {
            var devices = await _repo.GetAllDevicesAsync();
            var timeout = TimeSpan.FromSeconds(Math.Clamp(_options.PingTimeoutSeconds, 2, 15));
            List<HikSyncDevice> targets;
            (HikSyncDevice Dev, bool Up)[] results;
            if (_options.FullOnlineSweep)
            {
                targets = devices;
                results = await Task.WhenAll(targets.Select(async d => (Dev: d, Up: await _isapi.PingAsync(d, timeout))));
            }
            else
            {
                // Gentle mode: machines that were online (live ports) in parallel, plus a few rotating offline machines
                // one after another, a short pause apart. Rapid connects to many dead forwarded ports look like a port
                // scan to the office router (it blackholed the source on 2026-09-10).
                var online = devices.Where(d => d.Online).ToList();
                var offline = devices.Where(d => !d.Online).ToList();
                var probe = new List<HikSyncDevice>();
                if (offline.Count > 0)
                {
                    var idx = 0;
                    try { int.TryParse(await _repo.GetSettingAsync("offline_probe_idx"), out idx); } catch { /* start at 0 */ }
                    var n = Math.Min(offline.Count, Math.Max(1, _options.OfflineProbesPerPass));
                    for (var i = 0; i < n; i++) probe.Add(offline[(Math.Abs(idx) + i) % offline.Count]);
                    await SetSetting("offline_probe_idx", ((Math.Abs(idx) + n) % offline.Count).ToString());
                }
                targets = online.Concat(probe).ToList();
                if (targets.Count == 0) return "checked 0";

                var onlineTask = Task.WhenAll(online.Select(async d => (Dev: d, Up: await _isapi.PingAsync(d, timeout))));
                var probed = new List<(HikSyncDevice Dev, bool Up)>();
                foreach (var d in probe)
                {
                    if (probed.Count > 0) await Task.Delay(TimeSpan.FromMilliseconds(1500));
                    probed.Add((d, await _isapi.PingAsync(d, timeout)));
                }
                results = (await onlineTask).Concat(probed).ToArray();
            }

            var allDown = results.Length > 0 && results.All(r => !r.Up);
            if (allDown) _logger.LogWarning("HIK online check: all probed machines unreachable — marking them offline");
            await SetSetting("path_blocked_at", allDown ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() : "0");

            var changed = 0;
            var cameOnline = new List<HikSyncDevice>();
            foreach (var (dev, up) in results)
            {
                if (up && !dev.Online) { changed++; cameOnline.Add(dev); await Log(null, dev.Id, "online", true, "machine is reachable again"); }
                if (!up && dev.Online) { changed++; await Log(null, dev.Id, "offline", false, "machine stopped responding"); }
                await _repo.SetDeviceOnlineAsync(dev.Id, up);
                dev.Online = up;
            }

            // A machine that just came back is reconciled right away: clock, queued ops, pending grants, credentials.
            if (cameOnline.Count > 0)
            {
                await Task.WhenAll(cameOnline.Select(async d =>
                {
                    try { var r = await _isapi.SetDeviceTimeAsync(d, _options.DeviceTzOffsetMinutes); await Log(null, d.Id, "time-sync", r.Ok, "clock set after coming online"); }
                    catch { /* next daily sync catches it */ }
                }));
                try { await ReplayPendingOpsAsync(); } catch { /* retried by the watcher */ }
                try { await SyncAllPendingAsync(); } catch (Exception ex) { _logger.LogError(ex, "HIK pending grant sync after online failed"); }
                try { await RunCredentialSyncAsync(); } catch { /* retried by the watcher */ }
            }
            return $"checked {targets.Count}, changed {changed}" + (cameOnline.Count > 0 ? $", back online: {string.Join(", ", cameOnline.Select(d => d.Name))}" : "");
        }

        // ---- clock sync (runClockSync) ---------------------------------------------------------

        private async Task<string> RunClockSyncAsync()
        {
            var devices = (await _repo.GetAllDevicesAsync()).Where(d => d.Online).ToList();
            if (devices.Count == 0) return "no machines online";
            var now = DateTime.UtcNow;
            var ok = 0;
            await Task.WhenAll(devices.Select(async dev =>
            {
                try
                {
                    var devTime = await _isapi.GetDeviceTimeAsync(dev);
                    int? driftSec = devTime.HasValue ? (int)Math.Round(Math.Abs((now - devTime.Value).TotalSeconds)) : null;
                    var r = await _isapi.SetDeviceTimeAsync(dev, _options.DeviceTzOffsetMinutes);
                    var detail = driftSec != null ? $"clock synced to server (drift was {driftSec}s)" : (r.Ok ? "clock set to server time" : r.Error);
                    await Log(null, dev.Id, driftSec > 15 ? "clock-drift-warning" : "time-sync", r.Ok, detail);
                    if (r.Ok) Interlocked.Increment(ref ok);
                }
                catch (Exception ex) { await Log(null, dev.Id, "time-sync", false, ex.Message); }
            }));
            return $"{ok}/{devices.Count} machines synced";
        }

        // ---- expiry (runExpiryPass) ------------------------------------------------------------

        private async Task<string> RunExpiryPassAsync()
        {
            var now = NowLocalIso();
            var expired = await _repo.RunExpiryAsync(now); // flips status to 'expired', returns affected rows
            foreach (var emp in expired)
            {
                var empId = I(emp, "id");
                await Log(empId, null, "expire", true, $"expired at {now}");
                var autoDelete = emp.TryGetValue("auto_delete", out var ad) && ad != null && (ad is bool b ? b : ad.ToString() == "1" || string.Equals(ad.ToString(), "true", StringComparison.OrdinalIgnoreCase));
                if (!autoDelete) continue;

                // A CARD record's employee_no is its holder — remove the card, never the person.
                var rec = await _repo.GetEmployeeAsync(empId);
                var isCard = rec != null && S(rec, "kind") == "card";
                var cardNo = rec != null ? S(rec, "card_no") : null;
                foreach (var g in await _repo.GetGrantsForEmployeeAsync(empId))
                {
                    var dev = await _repo.GetDeviceAsync(I(g, "device_id"));
                    if (dev == null) continue;
                    try
                    {
                        var r = isCard && !string.IsNullOrEmpty(cardNo)
                            ? await _isapi.DeleteCardAsync(dev, cardNo!)
                            : await _isapi.DeletePersonAsync(dev, S(emp, "employee_no") ?? string.Empty);
                        await Log(empId, dev.Id, isCard ? "auto-delete-card" : "auto-delete", r.Ok, r.Ok ? "ok" : r.Error);
                    }
                    catch (Exception ex) { await Log(empId, dev.Id, "auto-delete", false, ex.Message); }
                }
                await _repo.DeleteGrantsForEmployeeAsync(empId);
            }
            return $"expired {expired.Count}";
        }

        // ---- grant push (sync.js) --------------------------------------------------------------

        private async Task PushEmployeeToDeviceAsync(IDictionary<string, object?> emp, HikSyncDevice dev)
        {
            var empNo = S(emp, "employee_no") ?? string.Empty;
            var person = new HikPersonRecord
            {
                EmployeeNo = empNo,
                Name = S(emp, "name") ?? $"User {empNo}",
                ValidBegin = FormatLocal(emp, "valid_begin") ?? "2020-01-01T00:00:00",
                ValidEnd = FormatLocal(emp, "valid_end") ?? "2037-12-31T23:59:59"
            };
            var r = await _isapi.WritePersonAsync(dev, person, modify: false);
            if (!r.Ok) r = await _isapi.WritePersonAsync(dev, person, modify: true);
            await Log(I(emp, "id"), dev.Id, "person", r.Ok, r.Ok ? "ok" : r.Error);
            ThrowIfFailed(r, "person push failed");

            var cardNo = S(emp, "card_no");
            if (!string.IsNullOrEmpty(cardNo))
            {
                var c = await _isapi.AddCardAsync(dev, empNo, cardNo!);
                await Log(I(emp, "id"), dev.Id, "card", c.Ok, c.Ok ? "ok" : c.Error);
            }
            await InvalidateRosterAsync(dev.Id);
        }

        private static string? FormatLocal(IDictionary<string, object?> r, string k) =>
            r.TryGetValue(k, out var v) && v != null
                ? v is DateTime d ? d.ToString("yyyy-MM-ddTHH:mm:ss") : v.ToString()!.Length >= 19 ? v.ToString()![..19] : v.ToString()
                : null;

        private async Task<int> SyncEmployeeAsync(int employeeId)
        {
            var emp = await _repo.GetEmployeeAsync(employeeId);
            if (emp == null) return 0;
            // Card records mirror the holder: pushing one as a person would overwrite the real member.
            if (S(emp, "kind") == "card" || string.IsNullOrEmpty(S(emp, "employee_no"))) return 0;

            var done = 0;
            foreach (var g in await _repo.GetGrantsForEmployeeAsync(employeeId))
            {
                var dev = await _repo.GetDeviceAsync(I(g, "device_id"));
                if (dev == null) continue;
                var grantId = I(g, "id");
                try
                {
                    if (S(g, "sync_state") == "removing")
                    {
                        var r = await _isapi.DeletePersonAsync(dev, S(emp, "employee_no")!); // cascades card/face/fp
                        await Log(employeeId, dev.Id, "delete", r.Ok, r.Ok ? "ok" : r.Error);
                        await InvalidateRosterAsync(dev.Id);
                        await _repo.DeleteGrantAsync(grantId);
                    }
                    else
                    {
                        await PushEmployeeToDeviceAsync(emp, dev);
                        await _repo.SetGrantStateAsync(grantId, "synced", null);
                    }
                    done++;
                }
                catch (Exception ex)
                {
                    await _repo.SetGrantStateAsync(grantId, "error", ex.Message);
                }
            }
            return done;
        }

        private async Task<string> SyncAllPendingAsync()
        {
            var ids = await _repo.GetPendingGrantEmployeeIdsAsync();
            var done = 0;
            foreach (var id in ids) done += await SyncEmployeeAsync(id);
            return $"{ids.Count} employee(s), {done} grant(s) applied";
        }

        // ---- booking renewals (routes/bookings.js migrateRenewedBookings) ------------------------

        private static string? AccessEndOf(IDictionary<string, object?> b)
        {
            // Access ends when the PAID period ends: BillingPeriodEnd (when set) wins over EndOn; midnight = end of day.
            var s = FormatLocal(b, "billEnd") ?? FormatLocal(b, "EndOn");
            if (s != null && s.EndsWith("T00:00:00")) s = s[..10] + "T23:59:59";
            return s;
        }

        private async Task<string> MigrateRenewedBookingsAsync()
        {
            int migrated = 0, extended = 0;
            var nowIso = NowLocalIso();

            async Task Retarget(string fromRef, string toRef, string newEnd, List<(int Id, string? ValidEnd)> attendees, string action)
            {
                await _repo.RetargetVisitorsAsync(fromRef, toRef, newEnd);
                foreach (var a in attendees)
                {
                    await _repo.ResetGrantsToPendingAsync(a.Id);
                    try { await SyncEmployeeAsync(a.Id); } catch { /* machine unreachable — the watcher retries */ }
                }
                await Log(null, null, action, true, new { from = fromRef, to = toRef, attendees = attendees.Count, newEnd });
            }

            foreach (var bookingRef in await _repo.GetBookingRefsAsync())
            {
                if (!int.TryParse(bookingRef.Length > 4 ? bookingRef[4..] : "", out var oldId) || oldId <= 0) continue;
                var oldB = await _repo.GetBookingAsync(oldId);
                if (oldB == null) continue;
                var attendees = await _repo.GetAttendeesByRefAsync(bookingRef);
                if (attendees.Count == 0) continue;

                var curEnd = AccessEndOf(oldB);
                // 1) Same-row extension: a new installment advanced BillingPeriodEnd (or EndOn was edited) — follow it.
                if (curEnd != null && string.CompareOrdinal(curEnd, nowIso) > 0)
                {
                    if (attendees.Any(a => (a.ValidEnd ?? "") != curEnd))
                    {
                        await Retarget(bookingRef, bookingRef, curEnd, attendees, "booking-extend");
                        extended += attendees.Count;
                    }
                    continue;
                }

                // 2) New-row renewal: earliest newer running booking for the same customer + space, already paid.
                if (!(oldB.TryGetValue("EndOn", out var endObj) && endObj is DateTime oldEnd)) continue;
                var next = await _repo.GetNextBookingAsync(I(oldB, "Id"), S(oldB, "CustomerCode"), I(oldB, "SpaceId"), oldEnd);
                if (next == null) continue;
                var nextEnd = AccessEndOf(next);
                if (nextEnd == null || string.CompareOrdinal(nextEnd, nowIso) <= 0) continue; // successor not paid yet
                var nextRef = $"WNB-{I(next, "Id")}";
                if (await _repo.RefHasAttendeesAsync(nextRef)) continue; // the new booking already has its own attendees
                await Retarget(bookingRef, nextRef, nextEnd, attendees, "booking-renew");
                migrated += attendees.Count;
            }
            return $"renewed {migrated}, extended {extended}";
        }

        // ---- snapshots (refreshSnapshots) ------------------------------------------------------

        private async Task<string> RefreshSnapshotsAsync(int maxMachines)
        {
            var devs = (await _repo.GetAllDevicesAsync()).Where(d => d.Online).ToList();
            if (devs.Count == 0) return "no machines online";
            var snaps = (await _repo.GetSnapshotsAsync()).ToDictionary(s => s.DeviceId);
            var now = _clock.Now;
            TimeSpan Age(DateTime? at) => at.HasValue ? now - at.Value : TimeSpan.MaxValue;

            var targets = devs
                .Select(d => (Dev: d, Oldest: snaps.TryGetValue(d.Id, out var s) ? (Age(s.UsersAt) > Age(s.CardsAt) ? Age(s.UsersAt) : Age(s.CardsAt)) : TimeSpan.MaxValue))
                .Where(x => x.Oldest > SnapshotStale)
                .OrderByDescending(x => x.Oldest)
                .Take(maxMachines)
                .ToList();

            var refreshed = 0;
            foreach (var (dev, _) in targets)
            {
                try
                {
                    await GetRosterAsync(dev, TimeSpan.FromMilliseconds(1)); // forces a live scan + snapshot save
                    await GetCardTableAsync(dev, TimeSpan.FromMilliseconds(1));
                    refreshed++;
                }
                catch { /* unreachable — next pass */ }
            }
            return $"refreshed {refreshed}/{targets.Count}";
        }

        // ---- card grants (syncCardGrants) ------------------------------------------------------

        private async Task<string> SyncCardGrantsAsync()
        {
            var emps = await _repo.GetActiveCardEmployeesAsync();
            if (emps.Count == 0) return "no card records";
            var snaps = await _repo.GetSnapshotsAsync();

            var snapped = new HashSet<int>();
            var byCard = new Dictionary<string, HashSet<int>>();
            var holderByCard = new Dictionary<string, string>();
            foreach (var s in snaps)
            {
                var list = ParseCards(s.Cards);
                if (list == null) continue;
                snapped.Add(s.DeviceId);
                foreach (var c in list)
                {
                    if (!byCard.TryGetValue(c.CardNo, out var set)) byCard[c.CardNo] = set = new HashSet<int>();
                    set.Add(s.DeviceId);
                    holderByCard.TryAdd(c.CardNo, c.EmployeeNo);
                }
            }
            var nameByEmp = new Dictionary<string, string>();
            foreach (var s in snaps)
                foreach (var u in ParseRoster(s.Users) ?? new List<JsonObject>())
                    nameByEmp.TryAdd(Emp(u), NameOf(u));

            int added = 0, removed = 0;
            foreach (var (id, cardNo) in emps)
            {
                var on = byCard.TryGetValue(cardNo, out var devsWithCard) ? devsWithCard : new HashSet<int>();
                // employee_no IS the holder's number while assigned, NULL when the card is unassigned.
                holderByCard.TryGetValue(cardNo, out var holder);
                try { await _repo.UpdateCardHolderAsync(id, holder != null && nameByEmp.TryGetValue(holder, out var nm) ? nm : null, holder); } catch { /* best effort */ }

                var have = await _repo.GetGrantsForEmployeeAsync(id);
                var haveDevs = have.Select(g => I(g, "device_id")).ToHashSet();
                foreach (var devId in on.Where(d => !haveDevs.Contains(d)))
                {
                    await _repo.InsertSyncedGrantAsync(id, devId);
                    added++;
                }
                foreach (var g in have)
                {
                    var devId = I(g, "device_id");
                    if (S(g, "sync_state") == "synced" && snapped.Contains(devId) && !on.Contains(devId))
                    {
                        await _repo.DeleteGrantAsync(I(g, "id"));
                        removed++;
                    }
                }
            }
            return $"added {added}, removed {removed}";
        }

        // ---- face vault (sweepFaceVault) -------------------------------------------------------

        private async Task<string> SweepFaceVaultAsync()
        {
            var devs = await _repo.GetAllDevicesAsync();
            var snaps = await _repo.GetSnapshotsAsync();
            var people = new Dictionary<string, (string Emp, string Name, List<int> DevIds)>();
            foreach (var s in snaps)
                foreach (var u in ParseRoster(s.Users) ?? new List<JsonObject>())
                {
                    if (Num(u, "numOfFace") == 0) continue;
                    var key = PersonKey(Emp(u), NameOf(u));
                    if (!people.TryGetValue(key, out var p)) people[key] = p = (Emp(u), NameOf(u), new List<int>());
                    p.DevIds.Add(s.DeviceId);
                }

            var have = await _repo.GetFaceVaultKeysAsync();
            var saved = 0;
            foreach (var (key, p) in people)
            {
                if (have.Contains(key)) continue;
                foreach (var id in p.DevIds)
                {
                    var dev = devs.FirstOrDefault(d => d.Id == id);
                    if (dev == null || !dev.Online) continue;
                    try
                    {
                        var faces = await _isapi.ReadFacesAsync(dev, p.Emp);
                        if (faces is { Count: > 0 }) { await _repo.SaveFaceTemplateAsync(p.Emp, p.Name, faces[0]); saved++; break; }
                    }
                    catch { /* try their next machine */ }
                }
            }
            return $"backed up {saved} of {people.Count} faces";
        }

        // ---- WN_HIK_Users rebuild (syncUsersTable) ---------------------------------------------

        private async Task<string> SyncUsersTableAsync()
        {
            var devs = (await _repo.GetAllDevicesAsync()).ToDictionary(d => d.Id);
            var snaps = (await _repo.GetSnapshotsAsync()).Where(s => s.Users != null).ToList();
            if (snaps.Count == 0) return "no snapshots";

            var people = new Dictionary<string, (string Emp, string Name, bool Admin, List<string> Machines, List<string> Rooms)>();
            foreach (var s in snaps)
            {
                if (!devs.TryGetValue(s.DeviceId, out var dev)) continue;
                foreach (var u in ParseRoster(s.Users) ?? new List<JsonObject>())
                {
                    var key = PersonKey(Emp(u), NameOf(u));
                    if (!people.TryGetValue(key, out var p)) people[key] = p = (Emp(u), NameOf(u), false, new List<string>(), new List<string>());
                    if (string.Equals(u["localUIRight"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase)) people[key] = p = p with { Admin = true };
                    if (!p.Machines.Contains(dev.Name)) p.Machines.Add(dev.Name);
                    if (!string.IsNullOrEmpty(dev.Code) && !dev.IsEntrance && !p.Rooms.Contains(dev.Code!)) p.Rooms.Add(dev.Code!);
                }
            }

            // CNIC and tag are dashboard data (not on machines) — carried across the rebuild.
            var meta = (await _repo.GetUserMetaAsync()).GroupBy(m => PersonKey(m.EmployeeNo, m.Name)).ToDictionary(g => g.Key, g => g.First());
            var rows = new List<HikUserRow>();
            foreach (var (key, p) in people)
            {
                meta.TryGetValue(key, out var m);
                rows.Add(new HikUserRow
                {
                    EmployeeNo = p.Emp, Name = p.Name, Room = p.Rooms.Count > 0 ? string.Join(",", p.Rooms) : null,
                    Role = p.Admin ? "admin" : "user", Machines = JsonSerializer.Serialize(p.Machines), MachineCount = p.Machines.Count,
                    Cnic = m?.Cnic, TagId = m?.TagId
                });
            }
            // Someone with CNIC/tag but in no snapshot yet keeps a minimal row, so the metadata is never lost.
            foreach (var (key, m) in meta.Where(x => !people.ContainsKey(x.Key)))
                rows.Add(new HikUserRow { EmployeeNo = m.EmployeeNo, Name = m.Name, Machines = "[]", MachineCount = 0, Cnic = m.Cnic, TagId = m.TagId });

            await _repo.RebuildUsersAsync(rows);
            return $"{people.Count} users";
        }

        // ---- events (archiveEvents) ------------------------------------------------------------

        private async Task<string> ArchiveEventsAsync()
        {
            var devices = (await _repo.GetAllDevicesAsync()).Where(d => d.Online).ToList();
            var saved = 0;
            var failed = new System.Collections.Concurrent.ConcurrentBag<string>();
            await Task.WhenAll(devices.Select(async dev =>
            {
                try
                {
                    var head = await _isapi.SearchEventsAsync(dev, 0, 1, TimeSpan.FromSeconds(2));
                    if (!head.Ok) { failed.Add(dev.Name); _logger.LogWarning("HIK events: {Device} could not be read", dev.Name); return; }
                    if (head.Total == 0) return;
                    // Firmware caps event pages at 30 — walk only the tail so background jobs don't stall.
                    var pos = Math.Max(0, head.Total - 60);
                    var recent = new List<JsonObject>();
                    while (pos < head.Total && recent.Count < 90)
                    {
                        var page = await _isapi.SearchEventsAsync(dev, pos, 30, TimeSpan.FromSeconds(2));
                        if (!page.Ok || page.List.Count == 0) break;
                        recent.AddRange(page.List);
                        pos += page.List.Count;
                    }

                    var (lastSerial, lastTime) = await _repo.GetLastEventAsync(dev.Id);
                    foreach (var e in recent)
                    {
                        long? serial = long.TryParse(e["serialNo"]?.ToString(), out var sn) && sn > 0 ? sn : null;
                        var t = e["time"]?.ToString() ?? string.Empty;
                        if (t.Length < 19) continue;
                        t = t[..19];
                        // Skip re-read tail events, but not when the time is newer: a factory-reset machine restarts serials at 1.
                        if (serial != null && serial <= lastSerial && lastTime != null && string.CompareOrdinal(t, lastTime) <= 0) continue;
                        if (!DateTime.TryParse(t, out var eventTime)) continue;

                        int? minor = int.TryParse(e["minor"]?.ToString(), out var mn) && mn != 0 ? mn : null;
                        var label = minor.HasValue && EventLabels.TryGetValue(minor.Value, out var l) ? l : $"Event {e["minor"]}";
                        var emp = e["employeeNoString"]?.ToString();
                        var card = e["cardNo"]?.ToString();
                        if (await _repo.TryInsertEventAsync(dev.Id, dev.Name, string.IsNullOrEmpty(emp) ? null : emp, e["name"]?.ToString(),
                                string.IsNullOrEmpty(card) ? null : card, minor, label, serial, eventTime))
                            Interlocked.Increment(ref saved);
                    }
                }
                catch (Exception ex)
                {
                    failed.Add(dev.Name);
                    _logger.LogWarning("HIK event archive failed for {Device}: {Error}", dev.Name, ex.Message);
                }
            }));
            var result = $"stored {saved} new event(s) from {devices.Count - failed.Count}/{devices.Count} online machine(s)";
            if (!failed.IsEmpty) result += $"; could not read: {string.Join(", ", failed.OrderBy(n => n).Take(10))}{(failed.Count > 10 ? "…" : "")}";
            if (devices.Count == 0) result = "no machines are marked online (run the online check / Test all first)";
            return result;
        }

        // ---- queued operations (replayPendingOps) ----------------------------------------------

        private async Task<string> ReplayPendingOpsAsync()
        {
            var ops = await _repo.GetPendingOpsAsync(200);
            if (ops.Count == 0) return "queue empty";
            var devCache = new Dictionary<int, HikSyncDevice?>();
            var applied = 0;
            foreach (var o in ops)
            {
                var opId = I(o, "id");
                var devId = I(o, "device_id");
                if (!devCache.TryGetValue(devId, out var dev)) devCache[devId] = dev = await _repo.GetDeviceAsync(devId);
                if (dev == null) { await _repo.DeletePendingOpAsync(opId); continue; }
                if (!dev.Online) continue; // wait for the online check to see it up

                var op = S(o, "op") ?? string.Empty;
                var emp = S(o, "employee_no") ?? string.Empty;
                try
                {
                    var payload = string.IsNullOrEmpty(S(o, "payload")) ? new JsonObject() : JsonNode.Parse(S(o, "payload")!) as JsonObject ?? new JsonObject();
                    switch (op)
                    {
                        case "grant":
                        {
                            var rec = payload["record"] as JsonObject ?? new JsonObject();
                            var person = new HikPersonRecord
                            {
                                EmployeeNo = rec["employeeNo"]?.ToString() ?? emp,
                                Name = rec["name"]?.ToString() ?? $"User {emp}",
                                ValidBegin = rec["validBegin"]?.ToString() ?? "2020-01-01T00:00:00",
                                ValidEnd = rec["validEnd"]?.ToString() ?? "2037-12-31T23:59:59",
                                Enabled = !string.Equals(rec["enabled"]?.ToString(), "false", StringComparison.OrdinalIgnoreCase),
                                Admin = string.Equals(rec["admin"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase)
                            };
                            var r = await _isapi.WritePersonAsync(dev, person, modify: false);
                            if (!r.Ok) r = await _isapi.WritePersonAsync(dev, person, modify: true);
                            ThrowIfFailed(r, "grant failed");
                            foreach (var c in payload["cards"] as JsonArray ?? new JsonArray()) await _isapi.AddCardAsync(dev, emp, c?.ToString() ?? "");
                            foreach (var fp in payload["prints"] as JsonArray ?? new JsonArray())
                                await _isapi.AddFingerprintAsync(dev, emp, fp?["fingerData"]?.ToString() ?? "",
                                    int.TryParse(fp?["fingerPrintID"]?.ToString(), out var fid) && fid > 0 ? fid : 1);
                            foreach (var f in payload["faces"] as JsonArray ?? new JsonArray()) await _isapi.AddFaceByModelAsync(dev, emp, f?.ToString() ?? "");
                            break;
                        }
                        case "block":
                        case "unblock":
                        case "rename":
                        case "set-role":
                        {
                            var (p, lookup) = await _isapi.GetPersonRecordAsync(dev, emp);
                            ThrowIfFailed(lookup, "user lookup failed");
                            if (p != null)
                            {
                                var enabledNow = !string.Equals(p["Valid"]?["enable"]?.ToString(), "false", StringComparison.OrdinalIgnoreCase);
                                var r = await _isapi.WritePersonAsync(dev, new HikPersonRecord
                                {
                                    EmployeeNo = emp,
                                    Name = op == "rename" ? payload["name"]?.ToString() ?? emp : p["name"]?.ToString() ?? $"User {emp}",
                                    Admin = op == "set-role"
                                        ? string.Equals(payload["admin"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase)
                                        : string.Equals(p["localUIRight"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase),
                                    Enabled = op == "block" ? false : op == "unblock" || enabledNow,
                                    ValidBegin = p["Valid"]?["beginTime"]?.ToString() ?? "2020-01-01T00:00:00",
                                    ValidEnd = p["Valid"]?["endTime"]?.ToString() ?? "2037-12-31T23:59:59"
                                }, modify: true);
                                ThrowIfFailed(r, $"{op} failed");
                            }
                            break;
                        }
                        case "add-fp":
                        {
                            var (p, lookup) = await _isapi.GetPersonRecordAsync(dev, emp);
                            ThrowIfFailed(lookup, "user lookup failed");
                            if (p != null)
                            {
                                var r = await _isapi.AddFingerprintAsync(dev, emp, payload["fingerData"]?.ToString() ?? "",
                                    int.TryParse(payload["fingerNo"]?.ToString(), out var fno) && fno > 0 ? fno : 1);
                                if (!r.Ok && !Regex.IsMatch(r.SubStatusCode ?? "", "alreadyexist", RegexOptions.IgnoreCase)) ThrowIfFailed(r, "add fingerprint failed");
                            }
                            break;
                        }
                        case "delete-user":
                        {
                            var r = await _isapi.DeletePersonAsync(dev, emp);
                            if (!r.Ok && !Regex.IsMatch(r.SubStatusCode ?? "", "notExist", RegexOptions.IgnoreCase)) ThrowIfFailed(r, "delete failed");
                            break;
                        }
                        case "door-control":
                        {
                            // A door command only makes sense for whoever is standing there NOW: never replay an old unlock.
                            var created = o.TryGetValue("created_at", out var ca) && ca is DateTime cdt ? cdt : (DateTime?)null;
                            var age = created.HasValue ? _clock.Now - created.Value : TimeSpan.MaxValue;
                            if (!(age >= TimeSpan.Zero && age < TimeSpan.FromMinutes(2)))
                                await Log(null, dev.Id, "dropped-stale-door-op", true, new { queuedAt = created });
                            else
                                ThrowIfFailed(await _isapi.RemoteControlDoorAsync(dev, payload["cmd"]?.ToString() ?? "open"), "door command failed");
                            break;
                        }
                    }
                    await _repo.DeletePendingOpAsync(opId);
                    await Log(null, dev.Id, $"applied-queued:{op}", true, new { employee_no = emp });
                    await InvalidateRosterAsync(dev.Id);
                    applied++;
                }
                catch (Exception ex)
                {
                    var msg = ex.Message.Length > 400 ? ex.Message[..400] : ex.Message;
                    await _repo.FailPendingOpAsync(opId, msg);
                    if (IsUnreachable(ex)) continue;
                    if (I(o, "attempts") + 1 >= 8)
                    {
                        await _repo.DeletePendingOpAsync(opId);
                        await Log(null, dev.Id, $"dropped-queued:{op}", false, msg);
                    }
                }
            }
            return $"applied {applied} of {ops.Count} queued op(s)";
        }

        // ---- credential sync (runCredentialSync / syncCredentialGroup) --------------------------

        private async Task<string> RunCredentialSyncAsync()
        {
            var devices = (await _repo.GetAllDevicesAsync()).Where(d => d.Online).ToList();
            if (devices.Count < 2) return "fewer than 2 machines online";

            var rosters = (await Task.WhenAll(devices.Select(async d =>
            {
                try { return (Dev: d, Users: await GetRosterAsync(d, RosterTtl)); }
                catch { return (Dev: d, Users: (List<JsonObject>?)null); } // unreachable — skip this machine this round
            }))).Where(r => r.Users != null).ToList();
            if (rosters.Count < 2) return "fewer than 2 rosters";

            // A person is matched ONLY when both employee # AND name are equal on the machines.
            var groups = new Dictionary<string, List<(HikSyncDevice Dev, JsonObject U)>>();
            foreach (var (dev, users) in rosters)
                foreach (var u in users!)
                {
                    var key = PersonKey(Emp(u), NameOf(u));
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<(HikSyncDevice, JsonObject)>();
                    list.Add((dev, u));
                }

            var copied = 0;
            foreach (var members in groups.Values.Where(m => m.Count >= 2))
                copied += await SyncCredentialGroupAsync(members);
            if (copied > 0) await InvalidateRosterAsync(null);
            return $"copied {copied} credential(s)";
        }

        /// <summary>Copy the union of ONE person's fingerprints, cards and face template to every machine lacking them.</summary>
        private async Task<int> SyncCredentialGroupAsync(List<(HikSyncDevice Dev, JsonObject U)> members)
        {
            var copied = 0;
            var employeeNo = Emp(members[0].U);
            var name = NameOf(members[0].U);

            // Fingerprints: union by finger slot; the DB vault is the primary source, machine exports are unioned in.
            try
            {
                var sets = (await Task.WhenAll(members.Select(async m => (M: m, Prints: await _isapi.ReadFingerprintsAsync(m.Dev, employeeNo)))))
                    .Where(s => s.Prints != null).ToList();
                var union = new Dictionary<int, string>();
                try { foreach (var v in await _repo.GetFpTemplatesAsync(employeeNo, name)) union[v.FingerPrintId] = v.FingerData; } catch { /* vault unavailable */ }
                foreach (var s in sets) foreach (var p in s.Prints!) union.TryAdd(p.FingerPrintId, p.FingerData);
                // Vault anything a machine exported, so it survives even if every machine holding it dies.
                foreach (var s in sets) foreach (var p in s.Prints!)
                    try { await _repo.SaveFpTemplateAsync(employeeNo, name, p.FingerPrintId, p.FingerData); } catch { /* best effort */ }

                await Task.WhenAll(sets.Select(async s =>
                {
                    try
                    {
                        if (union.Count > 0 && Num(s.M.U, "numOfFP") >= union.Count) return; // already complete
                        var have = s.Prints!.Select(p => p.FingerPrintId).ToHashSet();
                        foreach (var (fid, data) in union)
                        {
                            if (have.Contains(fid)) continue;
                            var r = await _isapi.AddFingerprintAsync(s.M.Dev, employeeNo, data, fid);
                            var ok = r.Ok || Regex.IsMatch(r.SubStatusCode ?? "", "alreadyexist", RegexOptions.IgnoreCase) || r.DuplicateWith != null;
                            await Log(null, s.M.Dev.Id, "sync-fingerprint", ok, new
                            {
                                employeeNo, fingerPrintID = fid, error = r.Ok ? null : r.Error,
                                duplicateWith = r.DuplicateWith, note = r.DuplicateWith != null ? $"Active under employee #{r.DuplicateWith}" : null
                            });
                            if (r.Ok || r.DuplicateWith != null) Interlocked.Increment(ref copied);
                        }
                    }
                    catch { /* this machine only — retried next round */ }
                }));
            }
            catch { /* partial failure — retried next round */ }

            // Cards: union of card numbers.
            try
            {
                var sets = (await Task.WhenAll(members.Select(async m => (M: m, Cards: await _isapi.ReadCardsAsync(m.Dev, employeeNo)))))
                    .Where(s => s.Cards != null).ToList();
                var union = sets.SelectMany(s => s.Cards!).ToHashSet();
                await Task.WhenAll(sets.Select(async s =>
                {
                    try
                    {
                        var have = s.Cards!.ToHashSet();
                        foreach (var c in union.Where(c => !have.Contains(c)))
                        {
                            var r = await _isapi.AddCardAsync(s.M.Dev, employeeNo, c);
                            var ok = r.Ok || Regex.IsMatch(r.SubStatusCode ?? "", "alreadyexist|duplicate", RegexOptions.IgnoreCase);
                            await Log(null, s.M.Dev.Id, "sync-card", ok, new { employeeNo, cardNo = c });
                            if (r.Ok) Interlocked.Increment(ref copied);
                        }
                    }
                    catch { /* this machine only */ }
                }));
            }
            catch { /* retried next round */ }

            // Faces: copy the recognition template to machines with no face enrolled.
            try
            {
                var withFace = members.Where(m => Num(m.U, "numOfFace") > 0).ToList();
                var without = members.Where(m => Num(m.U, "numOfFace") == 0).ToList();
                if (without.Count > 0)
                {
                    List<string>? faces = null;
                    if (withFace.Count > 0) faces = await _isapi.ReadFacesAsync(withFace[0].Dev, employeeNo);
                    if (faces is { Count: > 0 })
                    {
                        try { await _repo.SaveFaceTemplateAsync(employeeNo, name, faces[0]); } catch { /* keep the vault current — best effort */ }
                    }
                    else
                    {
                        var vaulted = await _repo.GetFaceTemplateAsync(employeeNo, name); // no machine can provide it — restore from the vault
                        if (vaulted != null) faces = new List<string> { vaulted };
                    }
                    if (faces is { Count: > 0 })
                        await Task.WhenAll(without.Select(async m =>
                        {
                            try
                            {
                                var r = await _isapi.AddFaceByModelAsync(m.Dev, employeeNo, faces[0]);
                                var ok = r.Ok || Regex.IsMatch(r.SubStatusCode ?? "", "alreadyexist", RegexOptions.IgnoreCase);
                                await Log(null, m.Dev.Id, "sync-face", ok, new { employeeNo });
                                if (r.Ok) Interlocked.Increment(ref copied);
                            }
                            catch { /* this machine only */ }
                        }));
                }
            }
            catch { /* retried next round */ }
            return copied;
        }

        // ---- enrollment watcher (runRosterWatch) -----------------------------------------------

        private async Task<string> RosterSignatureAsync()
        {
            var devices = (await _repo.GetAllDevicesAsync()).Where(d => d.Online).ToList();
            if (devices.Count == 0) return "none-online";
            var parts = new ConcurrentBag<string>();
            await Task.WhenAll(devices.Select(async dev =>
            {
                try
                {
                    foreach (var u in await GetRosterAsync(dev, TimeSpan.FromSeconds(5))) // near-fresh scan for the watcher
                        parts.Add($"{dev.Id}:{Emp(u)}:{u["name"]}:{Num(u, "numOfFP")}:{Num(u, "numOfFace")}:{Num(u, "numOfCard")}");
                }
                catch { parts.Add($"{dev.Id}:unreachable"); }
            }));
            return string.Join("|", parts.OrderBy(p => p, StringComparer.Ordinal));
        }

        private async Task<string> RunRosterWatchAsync()
        {
            try { await MigrateRenewedBookingsAsync(); } catch { /* checked again next tick */ }
            if (await _repo.CountPendingGrantsAsync() > 0)
            {
                try { await SyncAllPendingAsync(); } catch (Exception ex) { _logger.LogError(ex, "HIK watch: pending sync failed"); }
            }
            try { await ReplayPendingOpsAsync(); } catch (Exception ex) { _logger.LogError(ex, "HIK watch: queued-op replay failed"); }

            var sig = await RosterSignatureAsync();
            if (_rosterSignature != null && sig != _rosterSignature)
            {
                var r = await RunCredentialSyncAsync(); // enrollment changed at a terminal — copy it everywhere now
                _rosterSignature = await RosterSignatureAsync();
                return $"enrollment change detected — {r}";
            }
            _rosterSignature = sig;
            return "no enrollment change";
        }
    
        // ---- "Test" buttons --------------------------------------------------------------------

        public async Task<HikDeviceTestResult?> TestDeviceAsync(int deviceId)
        {
            var dev = await _repo.GetDeviceAsync(deviceId);
            return dev == null ? null : await TestAsync(dev, TimeSpan.FromSeconds(6));
        }

        public async Task<List<HikDeviceTestResult>> TestAllDevicesAsync(IReadOnlyCollection<int>? onlyDeviceIds = null)
        {
            var devices = await _repo.GetAllDevicesAsync();
            if (onlyDeviceIds != null) devices = devices.Where(d => onlyDeviceIds.Contains(d.Id)).ToList();
            var results = await Task.WhenAll(devices.Select(d => TestAsync(d, TimeSpan.FromSeconds(4))));
            return results.ToList();
        }

        /// <summary>Reads the machine's device info over ISAPI; saves the result (Online, Last_seen) and logs it.</summary>
        private async Task<HikDeviceTestResult> TestAsync(HikSyncDevice dev, TimeSpan timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            HikDeviceInfo info;
            try { info = await _isapi.GetDeviceInfoAsync(dev, timeout); }
            catch (Exception ex) { info = new HikDeviceInfo { Ok = false, Error = ex.Message }; }
            sw.Stop();
            try
            {
                await _repo.SetDeviceOnlineAsync(dev.Id, info.Ok);
                if (info.Ok != dev.Online) await InvalidateRosterAsync(dev.Id);
                await Log(null, dev.Id, "test", info.Ok, info.Ok ? $"online ({sw.ElapsedMilliseconds} ms)" : info.Error);
            }
            catch { /* the test result is still returned */ }
            return new HikDeviceTestResult
            {
                DeviceId = dev.Id,
                Name = dev.Name,
                Online = info.Ok,
                Error = info.Ok ? null : (string.IsNullOrWhiteSpace(info.Error) ? "The machine did not respond." : info.Error),
                Model = info.Model,
                SerialNumber = info.SerialNumber,
                FirmwareVersion = info.FirmwareVersion,
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }
}
}
