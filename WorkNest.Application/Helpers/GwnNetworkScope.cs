using WorkNest.Application.DTOs.Gwn;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Which Grandstream networks a caller sees. A network belongs to the locations saved for it in
    /// dbo.WN_GWN_NetworkLocations (F-7 = 2). Location-bound callers (admin, sales executive, sales manager) see only the
    /// networks of their locations — a network linked to no location is not theirs. Super admins and the CEO
    /// (<paramref name="myLocations"/> null) see every network.
    /// </summary>
    public static class GwnNetworkScope
    {
        /// <param name="myLocations">The caller's locations; null = every location (super admin, CEO).</param>
        /// <param name="locationId">The one location asked for; null = all of the caller's.</param>
        public static List<GwnNetworkDto> Visible(IEnumerable<GwnNetworkDto> networks, IReadOnlyCollection<int>? myLocations, int? locationId)
        {
            var list = networks.ToList();
            if (myLocations != null)
            {
                if (locationId is > 0 && !myLocations.Contains(locationId.Value)) return new();
                list = list.Where(n => n.LocationIds.Any(myLocations.Contains)).ToList();
            }
            if (locationId is > 0) list = list.Where(n => n.LocationIds.Contains(locationId.Value)).ToList();
            return list;
        }
    }
}
