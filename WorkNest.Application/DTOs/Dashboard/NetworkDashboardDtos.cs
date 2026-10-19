namespace WorkNest.Application.DTOs.Dashboard
{
    // Network (UniFi) section of the CEO dashboard (GET api/ceo/network) and the Sales Manager dashboard
    // (GET api/sales-manager/network): read-only, built from what the API already polls for the Network pages (sites,
    // devices, ISP metrics, the stored client-count history) plus the cached client list for client types. Limited to
    // the UniFi sites that serve the chosen location (UnifiSiteScope). Serialized camelCase; times UTC ISO 8601 with Z.

    /// <summary>
    /// What the caller may see: <see cref="MyLocations"/> = the Sales Manager's locations (null = every location: CEO,
    /// super admin); <see cref="LocationId"/> = the one chosen (null = all of them).
    /// </summary>
    public sealed record NetworkScope(IReadOnlyList<int>? MyLocations, int? LocationId);

    /// <summary>GET api/ceo/network, GET api/sales-manager/network</summary>
    public sealed class NetworkDashboardDto
    {
        /// <summary>
        /// False: UniFi is not set up on this server, not reachable yet, or its figures could not be read; only
        /// <see cref="Message"/> is filled. The rest of the dashboard does not depend on it.
        /// </summary>
        public bool Available { get; set; } = true;
        /// <summary>A short plain sentence for the section (unavailable, or no network linked to the location).</summary>
        public string? Message { get; set; }
        /// <summary>When the API last read the network (UTC).</summary>
        public string? UpdatedAt { get; set; }
        /// <summary>The last reading is more than 10 minutes old (monitoring paused or the network not answering).</summary>
        public bool Stale { get; set; }
        /// <summary>The location asked for; null = all (the caller's).</summary>
        public int? LocationId { get; set; }
        /// <summary>The UniFi sites behind the figures, with the locations each one serves.</summary>
        public List<NetworkSiteDto> Sites { get; set; } = new();
        public NetworkClientsDto? Clients { get; set; }
        public NetworkDevicesDto? Devices { get; set; }
        public NetworkClientTypesDto? ClientTypes { get; set; }
        public NetworkHistoryDto? History { get; set; }
        public NetworkInternetDto? Internet { get; set; }
        /// <summary>Critical and serious issues only (device offline, internet line down, packet loss...).</summary>
        public List<NetworkAlertDto> Alerts { get; set; } = new();
    }

    public sealed class NetworkSiteDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>The WorkNest locations (WN_Locations.Id) this site serves; empty with everyLocation = all.</summary>
        public List<int> LocationIds { get; set; } = new();
        public bool EveryLocation { get; set; }
        /// <summary>site (its own setting) | network (the whole network's setting) | every (nothing saved).</summary>
        public string Source { get; set; } = "every";
    }

    /// <summary>Clients connected right now, and the busiest moment of the last 24 hours.</summary>
    public sealed class NetworkClientsDto
    {
        public int Total { get; set; }
        public int Wifi { get; set; }
        public int Wired { get; set; }
        public int Guest { get; set; }
        /// <summary>Most clients at once in the last 24 hours (stored samples, one a minute); null without history.</summary>
        public int? Peak24h { get; set; }
        public string? Peak24hAt { get; set; }
        /// <summary>Average clients over the last 24 hours; null without history.</summary>
        public int? Avg24h { get; set; }
    }

    /// <summary>UniFi devices (access points, switches, gateways...), online against offline; not-installed placeholders left out.</summary>
    public sealed class NetworkDevicesDto
    {
        public int Total { get; set; }
        public int Online { get; set; }
        public int Offline { get; set; }
        /// <summary>Placeholders named "not installed" / "spare" (not counted above).</summary>
        public int NotInstalled { get; set; }
        /// <summary>Access points, switches, gateways first, then the rest.</summary>
        public List<NetworkDeviceTypeDto> ByType { get; set; } = new();
    }

    public sealed class NetworkDeviceTypeDto
    {
        /// <summary>UniFi type as the Network pages show it ("Access point", "Switch", "Console / Gateway", ...).</summary>
        public string Type { get; set; } = "";
        /// <summary>Plural label ("Access points", "Switches", "Gateways", ...).</summary>
        public string Label { get; set; } = "";
        public int Online { get; set; }
        public int Offline { get; set; }
        /// <summary>Names of the offline ones (at most 10).</summary>
        public List<string> OfflineNames { get; set; } = new();
    }

    /// <summary>Connected clients by kind (Mobile, Laptop / PC, Unknown, ... as the Clients page labels them).</summary>
    public sealed class NetworkClientTypesDto
    {
        /// <summary>False: the client list could not be read (or is not kept for these sites); see Message.</summary>
        public bool Available { get; set; }
        public string? Message { get; set; }
        public int Total { get; set; }
        /// <summary>Most first.</summary>
        public List<NetworkClientTypeDto> Items { get; set; } = new();
    }

    public sealed class NetworkClientTypeDto
    {
        /// <summary>phone | computer | tablet | printer | tv | camera | access | network | iot | other</summary>
        public string Kind { get; set; } = "";
        public string Label { get; set; } = "";
        public int Count { get; set; }
        public int Wifi { get; set; }
        public int Wired { get; set; }
    }

    /// <summary>Clients over time from the stored history (WN_UNIFI_History, one sample a minute, 7 days kept).</summary>
    public sealed class NetworkHistoryDto
    {
        /// <summary>Last 24 hours, 15-minute steps (average clients; peak = most at once in the step).</summary>
        public List<NetworkHistoryPointDto> Day { get; set; } = new();
        /// <summary>Last 7 days (from the first stored sample), hourly steps.</summary>
        public List<NetworkHistoryPointDto> Week { get; set; } = new();
        public int DayStepMinutes { get; set; } = 15;
        public int WeekStepMinutes { get; set; } = 60;
    }

    /// <summary>One step; the counts are null when no sample fell in it (a gap).</summary>
    public sealed class NetworkHistoryPointDto
    {
        /// <summary>Start of the step (UTC).</summary>
        public string T { get; set; } = "";
        public int? Wifi { get; set; }
        public int? Wired { get; set; }
        public int? Guest { get; set; }
        public int? Peak { get; set; }
    }

    /// <summary>Internet lines (WAN) and their health, from the UniFi cloud (ISP metrics: 5-minute values, last 24 hours).</summary>
    public sealed class NetworkInternetDto
    {
        public int LinesUp { get; set; }
        public int LinesTotal { get; set; }
        /// <summary>Lowest WAN uptime % of the sites (UniFi's own figure).</summary>
        public double? UptimePct { get; set; }
        /// <summary>Latest latency (ms); the highest of the sites.</summary>
        public double? LatencyMs { get; set; }
        /// <summary>Average latency over the last 24 hours (ms).</summary>
        public double? LatencyAvgMs { get; set; }
        /// <summary>Highest packet loss in a 5-minute step over the last 24 hours (%).</summary>
        public double? LossMaxPct { get; set; }
        public List<NetworkWanDto> Lines { get; set; } = new();
        /// <summary>Latency per site over the last 24 hours, 15-minute steps.</summary>
        public List<NetworkLatencySeriesDto> Latency { get; set; } = new();
    }

    public sealed class NetworkWanDto
    {
        public string Site { get; set; } = "";
        /// <summary>WAN1, WAN2, ...</summary>
        public string Label { get; set; } = "";
        /// <summary>Internet provider as UniFi reports it ("" when unknown).</summary>
        public string Isp { get; set; } = "";
        public bool Up { get; set; }
        /// <summary>Primary | Backup | "" (unknown).</summary>
        public string Role { get; set; } = "";
        /// <summary>Uptime % over 24 hours.</summary>
        public double? UptimePct { get; set; }
        public int Issues { get; set; }
    }

    public sealed class NetworkLatencySeriesDto
    {
        public string SiteId { get; set; } = "";
        public string Name { get; set; } = "";
        public List<NetworkLatencyPointDto> Points { get; set; } = new();
    }

    public sealed class NetworkLatencyPointDto
    {
        public string T { get; set; } = "";
        /// <summary>Average latency in the step (ms).</summary>
        public double? Avg { get; set; }
        /// <summary>Highest latency in the step (ms).</summary>
        public double? Peak { get; set; }
        /// <summary>Highest packet loss in the step (%).</summary>
        public double? Loss { get; set; }
    }

    public sealed class NetworkAlertDto
    {
        /// <summary>critical | serious</summary>
        public string Level { get; set; } = "";
        public string Text { get; set; } = "";
        /// <summary>Site or console name.</summary>
        public string Where { get; set; } = "";
    }

    /// <summary>GET api/unifi/site-locations (Network Overview, Sites table): each site and the locations it serves.</summary>
    public sealed class UnifiSiteLocationsDto
    {
        /// <summary>False before the database update NET_01 has run (sites then use the network-wide setting).</summary>
        public bool Available { get; set; } = true;
        public string? Message { get; set; }
        public bool CanEdit { get; set; }
        /// <summary>The whole network's locations (Network Overview chip); empty = every location.</summary>
        public List<int> NetworkLocationIds { get; set; } = new();
        public List<UnifiSiteLocationDto> Sites { get; set; } = new();
    }

    public sealed class UnifiSiteLocationDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<int> LocationIds { get; set; } = new();
        public bool EveryLocation { get; set; }
        /// <summary>site | network | every</summary>
        public string Source { get; set; } = "every";
    }
}
