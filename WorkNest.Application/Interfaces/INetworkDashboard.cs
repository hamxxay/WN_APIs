using WorkNest.Application.DTOs.Dashboard;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Network (UniFi) section of the CEO and Sales Manager dashboards, and the per-site location setting behind it.
    /// Read-only for the dashboards: it never calls UniFi beyond what the Network pages already use (the polled
    /// summary, the stored client-count history, the cached client list and the Clients page's cached "Top data users" reads).
    /// </summary>
    public interface INetworkDashboardService
    {
        /// <summary>
        /// The network figures of the sites that serve the caller's scope (see UnifiSiteScope). Never throws: when UniFi
        /// is not set up, not reachable or unreadable, Available = false with a short plain Message.
        /// </summary>
        Task<NetworkDashboardDto> GetAsync(NetworkScope scope, CancellationToken ct = default);

        /// <summary>Each polled site with the locations it serves (Network Overview). Before NET_01: Available = false.</summary>
        Task<UnifiSiteLocationsDto> GetSiteLocationsAsync(bool canEdit);

        /// <summary>
        /// Saves the locations one site serves (empty = use the network-wide setting again). Null when the site is not on
        /// the network (last poll). Throws <see cref="UnifiSiteLocationsMissingException"/> before NET_01 has run.
        /// </summary>
        Task<UnifiSiteLocationsDto?> SetSiteLocationsAsync(string siteId, IReadOnlyList<int> locationIds, string? byEmail);
    }

    /// <summary>
    /// The database update NET_01_unifi_site_locations has not run: no dbo.WN_UNIFI_SiteLocations / its procedures
    /// (SQL errors 208 / 2812). Reading treats every site as unmapped (the network-wide setting applies); saving answers 503.
    /// </summary>
    public sealed class UnifiSiteLocationsMissingException : Exception
    {
        /// <summary>What the CMS shows (503). No "sql" in it: the CMS hides messages containing that word.</summary>
        public const string UserMessage =
            "Database update needed: run the script NET_01_unifi_site_locations_TEST (NET_01_unifi_site_locations_LIVE on the live database).";

        public UnifiSiteLocationsMissingException(Exception inner) : base(UserMessage, inner) { }
    }
}
