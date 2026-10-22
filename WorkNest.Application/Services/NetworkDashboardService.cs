using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Network (UniFi) section of the CEO and Sales Manager dashboards. Which sites a caller sees is decided here, on the
    /// server (<see cref="UnifiSiteScope"/>): a site serves the locations saved for it (dbo.WN_UNIFI_SiteLocations,
    /// script NET_01), else the whole network's locations (Network Overview chip), else every location. The figures come
    /// from <see cref="IUnifiService"/>'s polled state; nothing here writes to UniFi. Never throws for UniFi trouble:
    /// the section then gets Available = false and a short plain message, and the rest of the dashboard is unaffected.
    /// </summary>
    public sealed class NetworkDashboardService : INetworkDashboardService
    {
        public const string NotSetUpMessage = "Network monitoring is not set up on this server.";
        public const string TurnedOffMessage = "Network monitoring is turned off on this server.";
        public const string PausedMessage = "Network monitoring is paused. A super admin can start it again on the Services page.";
        public const string NotReachableMessage = "The network could not be reached. Its figures show here once it answers.";
        public const string NoDataYetMessage = "No network figures yet. They show a few minutes after the server starts.";
        public const string ReadFailedMessage = "The network figures could not be read right now. Please try again shortly.";
        public const string NoSiteForLocationMessage = "No network is linked to this location yet.";
        public const string NoSiteForLocationsMessage = "No network is linked to these locations yet.";

        /// <summary>How long a dashboard waits for the client list (client types) before showing the rest without it.</summary>
        private static readonly TimeSpan ClientsWait = TimeSpan.FromSeconds(6);
        /// <summary>Older figures are marked stale (polling runs every minute).</summary>
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

        private readonly IUnifiService _unifi;
        private readonly IDbRepository _db;
        private readonly IConfiguration _config;
        private readonly IServiceSwitch? _switches;
        private readonly ILogger<NetworkDashboardService> _logger;

        public NetworkDashboardService(IUnifiService unifi, IDbRepository db, IConfiguration config, ILogger<NetworkDashboardService> logger,
            IServiceSwitch? switches = null)
        {
            _unifi = unifi;
            _db = db;
            _config = config;
            _logger = logger;
            _switches = switches;
        }

        public async Task<NetworkDashboardDto> GetAsync(NetworkScope scope, CancellationToken ct = default)
        {
            var locationId = scope.LocationId is > 0 ? scope.LocationId : null;
            try
            {
                if (!_unifi.IsConfigured) return Unavailable(NotSetUpMessage, locationId);
                var paused = !await PollingOnAsync(ct);
                var sites = _unifi.GetSites();
                var (updatedAt, failing) = _unifi.GetPollState();
                if (sites.Count == 0)
                {
                    var message = string.Equals(_config["Unifi:Enabled"], "false", StringComparison.OrdinalIgnoreCase) ? TurnedOffMessage
                        : paused ? PausedMessage
                        : failing ? NotReachableMessage
                        : NoDataYetMessage;
                    return Unavailable(message, locationId);
                }

                var (resolved, _) = await ResolveAsync(sites);
                var visible = UnifiSiteScope.Visible(resolved, scope.MyLocations, locationId);
                var stale = updatedAt == null || DateTime.UtcNow - updatedAt.Value > StaleAfter;
                var updatedText = updatedAt?.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
                if (visible.Count == 0)
                {
                    return new NetworkDashboardDto
                    {
                        Available = true,
                        Message = locationId != null ? NoSiteForLocationMessage : NoSiteForLocationsMessage,
                        LocationId = locationId,
                        UpdatedAt = updatedText,
                        Stale = stale
                    };
                }

                var dto = await _unifi.GetNetworkSectionAsync(visible.Select(v => v.Site.Id).ToList(), ClientsWait, scope.Usage);
                dto.Available = true;
                dto.LocationId = locationId;
                dto.UpdatedAt = updatedText;
                dto.Stale = stale;
                dto.Message = stale && paused ? PausedMessage : null;
                dto.Sites = visible.Select(v => new NetworkSiteDto
                {
                    Id = v.Site.Id,
                    Name = v.Site.Name,
                    LocationIds = v.LocationIds.ToList(),
                    EveryLocation = v.EveryLocation,
                    Source = v.SourceKey
                }).ToList();
                return dto;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Network section of a dashboard failed: {Error}", ex.Message);
                return Unavailable(ReadFailedMessage, locationId);
            }
        }

        public async Task<UnifiSiteLocationsDto> GetSiteLocationsAsync(bool canEdit)
        {
            var (resolved, available) = await ResolveAsync(_unifi.GetSites());
            return new UnifiSiteLocationsDto
            {
                Available = available,
                Message = available ? null : UnifiSiteLocationsMissingException.UserMessage,
                CanEdit = canEdit,
                NetworkLocationIds = (await _unifi.GetLocationIdsAsync()).ToList(),
                Sites = resolved.Select(r => new UnifiSiteLocationDto
                {
                    Id = r.Site.Id,
                    Name = r.Site.Name,
                    LocationIds = r.LocationIds.ToList(),
                    EveryLocation = r.EveryLocation,
                    Source = r.SourceKey
                }).ToList()
            };
        }

        public async Task<UnifiSiteLocationsDto?> SetSiteLocationsAsync(string siteId, IReadOnlyList<int> locationIds, string? byEmail)
        {
            var site = _unifi.GetSites().FirstOrDefault(s => string.Equals(s.Id, (siteId ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (site == null) return null;
            var ids = locationIds.Where(id => id > 0).Distinct().OrderBy(id => id).ToList();
            await _db.SetUnifiSiteLocationsDbAsync(site.Id, site.Name, ids, byEmail);
            return await GetSiteLocationsAsync(true);
        }

        /// <summary>Each site with its locations; available = false before NET_01 (then no site has rows of its own).</summary>
        private async Task<(List<UnifiSiteLocations> Sites, bool Available)> ResolveAsync(IReadOnlyList<UnifiSiteRef> sites)
        {
            IReadOnlyList<(string SiteId, int LocationId)> rows;
            var available = true;
            try
            {
                rows = await _db.GetUnifiSiteLocationsDbAsync();
            }
            catch (UnifiSiteLocationsMissingException)
            {
                rows = Array.Empty<(string, int)>();
                available = false;
            }
            var network = await _unifi.GetLocationIdsAsync();
            return (UnifiSiteScope.Resolve(sites, rows, network.ToList()), available);
        }

        /// <summary>The UniFi polling service's on/off switch (Services page); on when it cannot be read.</summary>
        private async Task<bool> PollingOnAsync(CancellationToken ct)
        {
            if (_switches == null) return true;
            try { return await _switches.IsEnabledAsync(BackgroundServiceCatalog.UnifiPolling, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return true; }
        }

        private static NetworkDashboardDto Unavailable(string message, int? locationId) =>
            new() { Available = false, Message = message, LocationId = locationId };
    }
}
