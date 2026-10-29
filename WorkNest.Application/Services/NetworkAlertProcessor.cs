using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.Interfaces;
using static WorkNest.Application.Services.NetworkAlertPlanner;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// One check of the network alerts: for every location with an active recipient it reads what is down (access machines
    /// from dbo.WN_HIK_Devices, which HIK Sync keeps up to date; internet lines and UniFi devices and Grandstream devices
    /// from <see cref="INetworkDashboardService"/>, the same polled figures as the dashboards: nothing here polls a machine
    /// or a network), asks <see cref="NetworkAlertPlanner"/> what to send and sends ONE combined e-mail per location.
    /// Idempotent across API instances: dbo.WN_NetworkAlert_Claim leases the location for the check, and state is saved in
    /// the database. A kind that cannot be read this time (UniFi stale or not reachable, SQL error) is left as it is:
    /// nothing is alerted or marked recovered from missing data. An e-mail that could not be sent saves no alert mark, so
    /// the next check sends it again.
    /// </summary>
    public class NetworkAlertProcessor
    {
        public const int DefaultDownMinutes = 5;
        public const int DefaultReminderHours = 6;
        public const int DefaultTickSeconds = 60;
        private const int LeaseSeconds = 300;

        private static readonly HashSet<string> FullKinds = new() { NetworkAlertKinds.Hik, NetworkAlertKinds.Wan };

        private readonly INetworkAlertRepository _repo;
        private readonly INetworkDashboardService _network;
        private readonly IEmailService _email;
        private readonly IBusinessClock _clock;
        private readonly ILogger<NetworkAlertProcessor> _logger;

        public NetworkAlertProcessor(INetworkAlertRepository repo, INetworkDashboardService network, IEmailService email, IBusinessClock clock,
            IConfiguration configuration, ILogger<NetworkAlertProcessor> logger)
        {
            _repo = repo;
            _network = network;
            _email = email;
            _clock = clock;
            _logger = logger;
            Settings = new NetworkAlertSettings(
                TimeSpan.FromMinutes(PositiveInt(configuration["NetworkAlerts:DownMinutes"], DefaultDownMinutes)),
                TimeSpan.FromHours(PositiveInt(configuration["NetworkAlerts:ReminderHours"], DefaultReminderHours)));
            TickSeconds = PositiveInt(configuration["NetworkAlerts:TickSeconds"], DefaultTickSeconds);
        }

        public NetworkAlertSettings Settings { get; }
        public int TickSeconds { get; }

        /// <summary>What one check did (for the log and the tests).</summary>
        public sealed record TickResult(int Locations, int Emails, int Skipped);

        public async Task<TickResult> RunOnceAsync(CancellationToken ct = default)
        {
            int locations = 0, emails = 0, skipped = 0;
            foreach (var location in await _repo.GetActiveLocationsAsync(ct))
            {
                if (ct.IsCancellationRequested) break;
                var claim = await _repo.ClaimAsync(location.LocationId, LeaseSeconds, Math.Max(1, TickSeconds / 2), ct);
                if (!claim.Claimed) { skipped++; continue; }
                locations++;
                try
                {
                    if (await CheckLocationAsync(location, claim.DbNow, ct)) emails++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (NetworkAlertsMissingException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Network alert check of {Location} failed.", location.Name);
                }
                finally
                {
                    try { await _repo.ReleaseAsync(location.LocationId, CancellationToken.None); }
                    catch (Exception ex) { _logger.LogWarning("Network alert lease of {Location} was not released: {Error}", location.Name, ex.Message); }
                }
            }
            return new TickResult(locations, emails, skipped);
        }

        /// <summary>True when an e-mail went out for this location.</summary>
        private async Task<bool> CheckLocationAsync(NetworkAlertLocation location, DateTime now, CancellationToken ct)
        {
            var (items, kinds) = await ObserveAsync(location.LocationId, ct);
            if (kinds.Count == 0) return false;
            var saved = await _repo.GetStatesAsync(location.LocationId, ct);
            var plan = Plan(now, items, kinds, FullKinds, saved, Settings);

            foreach (var s in plan.Immediate) await _repo.SaveStateAsync(location.LocationId, s, ct);
            if (plan.Message == null) return false;

            var subject = NetworkAlertEmail.Subject(location.Name, plan.Message);
            var html = NetworkAlertEmail.Html(location.Name, plan.Message, now, _clock.FromUtc);
            var result = await SendAsync(location.Emails, subject, html);
            if (result.Delivered == 0)
            {
                _logger.LogWarning("Network alert for {Location} not sent ({Error}); it is tried again at the next check.", location.Name, result.Error);
                return false;
            }
            foreach (var s in plan.AfterEmail) await _repo.SaveStateAsync(location.LocationId, s, ct);
            await _repo.AddLogAsync(new NetworkAlertLogEntry(location.LocationId, plan.Message.Kind, subject, NetworkAlertEmail.Summary(plan.Message),
                string.Join(", ", result.Recipients), result.Held == result.Delivered ? "Held" : "Sent", result.Error), CancellationToken.None);
            _logger.LogInformation("Network alert for {Location}: {Subject} -> {Recipients}", location.Name, subject, string.Join(", ", result.Recipients));
            return true;
        }

        /// <summary>The test e-mail: to the location's active recipients. Throws InvalidOperationException when it has none or none could be sent.</summary>
        public async Task<IReadOnlyList<string>> SendTestAsync(int locationId, CancellationToken ct = default)
        {
            var location = (await _repo.GetActiveLocationsAsync(ct)).FirstOrDefault(l => l.LocationId == locationId)
                ?? throw new InvalidOperationException("This location has no active recipient. Add one first.");
            var now = DateTime.UtcNow;
            var (subject, html) = NetworkAlertEmail.Test(location.Name, now, _clock.FromUtc);
            var result = await SendAsync(location.Emails, subject, html);
            if (result.Delivered == 0) throw new InvalidOperationException($"The test e-mail could not be sent: {result.Error}");
            await _repo.AddLogAsync(new NetworkAlertLogEntry(locationId, "test", subject, "Test e-mail", string.Join(", ", result.Recipients),
                result.Held == result.Delivered ? "Held" : "Sent", result.Error), CancellationToken.None);
            return result.Recipients;
        }

        private sealed record SendResult(List<string> Recipients, int Delivered, int Held, string? Error);

        /// <summary>One e-mail per recipient. Held (Email Sending is off: kept in Pending emails) counts as delivered.</summary>
        private async Task<SendResult> SendAsync(IReadOnlyList<string> to, string subject, string html)
        {
            var ok = new List<string>();
            var errors = new List<string>();
            var held = 0;
            foreach (var address in to)
            {
                try
                {
                    await _email.SendHtmlReportAsync(address, subject, html);
                    ok.Add(address);
                }
                catch (EmailHeldException)
                {
                    ok.Add(address);
                    held++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{address}: {ex.Message}");
                }
            }
            return new SendResult(ok, ok.Count, held, errors.Count == 0 ? null : string.Join("; ", errors));
        }

        /// <summary>What is seen now, and which kinds could be read.</summary>
        private async Task<(List<NetworkAlertItem> Items, HashSet<string> Kinds)> ObserveAsync(int locationId, CancellationToken ct)
        {
            var items = new List<NetworkAlertItem>();
            var kinds = new HashSet<string>();

            try
            {
                foreach (var d in await _repo.GetHikDevicesAsync(locationId, ct))
                {
                    var detail = string.IsNullOrWhiteSpace(d.Host) ? "" : $"IP {d.Host}";
                    if (!d.Online && d.LastSeen is { } seen) detail += (detail == "" ? "" : ", ") + $"last seen {NetworkAlertEmail.Time(seen)}";
                    items.Add(new NetworkAlertItem(NetworkAlertKinds.Hik, d.Id.ToString(), d.Name, detail, !d.Online));
                }
                kinds.Add(NetworkAlertKinds.Hik);
            }
            catch (NetworkAlertsMissingException) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { _logger.LogWarning("Network alerts: access machines of location {Location} not read: {Error}", locationId, ex.Message); }

            try
            {
                var dto = await _network.GetAsync(new NetworkScope(null, locationId), ct);
                // UniFi: only fresh figures of a site that serves this location (a stale poll says nothing about now).
                if (dto is { Available: true, Stale: false })
                {
                    if (dto.Devices != null)
                    {
                        foreach (var t in dto.Devices.ByType)
                            foreach (var name in t.OfflineNames)
                                items.Add(new NetworkAlertItem(NetworkAlertKinds.Unifi, $"{t.Type}|{name}", name, SingularLabel(t.Type), true));
                        kinds.Add(NetworkAlertKinds.Unifi);
                    }
                    if (dto.Internet is { Lines.Count: > 0 } net) { items.AddRange(WanItems(net.Lines)); kinds.Add(NetworkAlertKinds.Wan); }
                }
                // Grandstream: devices only (GWN gives no internet-line status).
                if (dto.Gwn is { Available: true, Devices: not null } gwn)
                {
                    foreach (var d in gwn.Devices.List)
                        items.Add(new NetworkAlertItem(NetworkAlertKinds.Gwn, d.Mac, string.IsNullOrWhiteSpace(d.Name) ? d.Mac : d.Name,
                            string.IsNullOrWhiteSpace(d.Model) ? "" : d.Model, d.Status != "online"));
                    kinds.Add(NetworkAlertKinds.Gwn);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { _logger.LogWarning("Network alerts: network figures of location {Location} not read: {Error}", locationId, ex.Message); }

            return (items, kinds);
        }

        /// <summary>
        /// A line that is down is an item when it is the main line, or when no line is up. A backup line that is down while
        /// the main line works is not an outage. A main line down with another line up = failover to the backup line.
        /// </summary>
        public static IEnumerable<NetworkAlertItem> WanItems(IReadOnlyList<NetworkWanDto> lines)
        {
            var anyUp = lines.Any(l => l.Up);
            var backupUp = lines.FirstOrDefault(l => l.Up && l.Role == "Backup") ?? lines.FirstOrDefault(l => l.Up);
            foreach (var l in lines)
            {
                var name = string.IsNullOrWhiteSpace(l.Isp) ? l.Label : $"{l.Label} ({l.Isp})";
                var down = !l.Up && (l.Role != "Backup" || !anyUp);
                var detail = !down ? "" : !anyUp ? "No internet line is up" : $"{FailoverDetail} {backupUp!.Label}";
                yield return new NetworkAlertItem(NetworkAlertKinds.Wan, $"{l.Site}|{l.Label}", $"Internet {name}", detail, down);
            }
        }

        private static string SingularLabel(string type) => type switch { "Console / Gateway" => "Gateway", _ => type };

        private static int PositiveInt(string? value, int fallback) => int.TryParse(value, out var n) && n > 0 ? n : fallback;
    }
}
