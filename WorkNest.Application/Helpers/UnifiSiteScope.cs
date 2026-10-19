namespace WorkNest.Application.Helpers
{
    /// <summary>One UniFi site as the last cloud poll saw it (site id, display name, the console it runs on).</summary>
    public sealed record UnifiSiteRef(string Id, string Name, string? HostId);

    /// <summary>Where a site's WorkNest locations come from.</summary>
    public enum UnifiSiteLocationSource
    {
        /// <summary>The site's own rows in dbo.WN_UNIFI_SiteLocations (script NET_01, set by a super admin).</summary>
        Site,
        /// <summary>No own rows: the whole network's locations (WN_UNIFI_Settings "LocationIds", the Network Overview chip).</summary>
        Network,
        /// <summary>Nothing saved anywhere: the site serves every location (as the Network pages read an empty setting).</summary>
        Every
    }

    /// <summary>A site with the WorkNest locations it serves.</summary>
    public sealed record UnifiSiteLocations(UnifiSiteRef Site, IReadOnlyList<int> LocationIds, UnifiSiteLocationSource Source)
    {
        public bool EveryLocation => Source == UnifiSiteLocationSource.Every;

        public bool Serves(int locationId) => EveryLocation || LocationIds.Contains(locationId);

        public string SourceKey => Source switch
        {
            UnifiSiteLocationSource.Site => "site",
            UnifiSiteLocationSource.Network => "network",
            _ => "every"
        };
    }

    /// <summary>
    /// Ties UniFi sites to WorkNest locations (WN_Locations) for the CEO / Sales Manager network sections. A site's own
    /// rows (dbo.WN_UNIFI_SiteLocations) win; a site without rows (or before NET_01 has run) uses the network-wide
    /// "LocationIds" setting saved on Network Overview; when that is empty too, the site serves every location.
    /// Pure functions: the server-side scoping of a Sales Manager to their locations is decided here.
    /// </summary>
    public static class UnifiSiteScope
    {
        /// <summary>Each polled site with its locations and where they come from (rows of unknown sites are ignored).</summary>
        public static List<UnifiSiteLocations> Resolve(
            IEnumerable<UnifiSiteRef> sites,
            IEnumerable<(string SiteId, int LocationId)> siteRows,
            IReadOnlyCollection<int> networkLocationIds)
        {
            var own = siteRows
                .Where(r => !string.IsNullOrWhiteSpace(r.SiteId) && r.LocationId > 0)
                .GroupBy(r => r.SiteId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(r => r.LocationId).Distinct().OrderBy(x => x).ToList(),
                    StringComparer.OrdinalIgnoreCase);
            var network = networkLocationIds.Where(id => id > 0).Distinct().OrderBy(x => x).ToList();

            return sites.Select(s =>
            {
                if (own.TryGetValue(s.Id, out var ids) && ids.Count > 0)
                    return new UnifiSiteLocations(s, ids, UnifiSiteLocationSource.Site);
                return network.Count > 0
                    ? new UnifiSiteLocations(s, network, UnifiSiteLocationSource.Network)
                    : new UnifiSiteLocations(s, Array.Empty<int>(), UnifiSiteLocationSource.Every);
            }).ToList();
        }

        /// <summary>
        /// The sites a caller may see. <paramref name="myLocations"/>: null = every location (CEO, super admin), else the
        /// caller's assigned locations (Sales Manager; none = nothing). <paramref name="locationId"/>: the one location
        /// chosen (null = all of the caller's). A location outside <paramref name="myLocations"/> gives nothing, whatever
        /// the controller checked before.
        /// </summary>
        public static List<UnifiSiteLocations> Visible(IEnumerable<UnifiSiteLocations> sites, IReadOnlyCollection<int>? myLocations, int? locationId)
        {
            var list = sites.ToList();
            if (myLocations != null)
            {
                if (myLocations.Count == 0) return new List<UnifiSiteLocations>();
                if (locationId is > 0 && !myLocations.Contains(locationId.Value)) return new List<UnifiSiteLocations>();
            }
            if (locationId is > 0) return list.Where(s => s.Serves(locationId.Value)).ToList();
            if (myLocations == null) return list;
            return list.Where(s => s.EveryLocation || s.LocationIds.Any(myLocations.Contains)).ToList();
        }
    }
}
