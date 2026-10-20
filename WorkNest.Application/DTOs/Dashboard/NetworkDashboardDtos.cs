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
        /// <summary>
        /// The 5 clients (devices) that used the most data over <see cref="TopClientsRangeLabel"/>, most first (the Clients
        /// page's "Top data users", same cached reads). Empty = no usage recorded yet; null = not available (another
        /// console's site, still loading after the wait, or unreadable), with <see cref="TopClientsMessage"/> saying why.
        /// </summary>
        public List<NetworkTopClientDto>? TopClients { get; set; }
        /// <summary>The range behind <see cref="TopClients"/>: "Today" (UniFi's daily report, so since midnight, site time).</summary>
        public string? TopClientsRangeLabel { get; set; }
        /// <summary>The range in days (1 = today).</summary>
        public int? TopClientsRangeDays { get; set; }
        /// <summary>A short plain sentence when <see cref="TopClients"/> is null.</summary>
        public string? TopClientsMessage { get; set; }
    }

    /// <summary>One of the top data users (bytes; times UTC ISO 8601 with Z).</summary>
    public sealed class NetworkTopClientDto
    {
        public int Rank { get; set; }
        /// <summary>Alias, else UniFi name, else hostname, else a made-up name ("Apple phone · 3f:a1"), as the Clients page names it.</summary>
        public string Name { get; set; } = "";
        /// <summary>phone | computer | tablet | printer | tv | camera | access | network | iot | other</summary>
        public string Kind { get; set; } = "";
        /// <summary>Mobile, Laptop / PC, Unknown, ... as the Clients page labels it.</summary>
        public string KindLabel { get; set; } = "";
        /// <summary>aa:bb:cc:dd:ee:ff</summary>
        public string Mac { get; set; } = "";
        public string Ip { get; set; } = "";
        public string Hostname { get; set; } = "";
        /// <summary>Maker as UniFi reports it ("" when unknown).</summary>
        public string Vendor { get; set; } = "";
        public double DownloadBytes { get; set; }
        public double UploadBytes { get; set; }
        public double TotalBytes { get; set; }
        /// <summary>Connected right now.</summary>
        public bool Online { get; set; }
        /// <summary>Last seen on the network (UTC); null when unknown.</summary>
        public string? LastSeen { get; set; }
        public bool Wired { get; set; }
        public bool Guest { get; set; }
        /// <summary>Network (VLAN) name ("" when unknown).</summary>
        public string Network { get; set; } = "";
        /// <summary>Wi-Fi name; "" for wired clients or when unknown.</summary>
        public string Ssid { get; set; } = "";
        /// <summary>The access point (Wi-Fi) or switch (wired) it connects through; "" when unknown.</summary>
        public string Uplink { get; set; } = "";
        /// <summary>Switch port for wired clients; null when unknown.</summary>
        public string? Port { get; set; }
        /// <summary>One line for the dashboard: "WorkNest via AP Lobby", "Wired · USW 24 port 5", ...</summary>
        public string Connection { get; set; } = "";
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

    /// <summary>Clients connected right now (the history over time is in <see cref="NetworkHistoryDto"/>).</summary>
    public sealed class NetworkClientsDto
    {
        public int Total { get; set; }
        public int Wifi { get; set; }
        public int Wired { get; set; }
        public int Guest { get; set; }
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
        /// <summary>Why some or all lines have no live speed figures (still loading, not readable, other console); null = all have them.</summary>
        public string? SpeedsMessage { get; set; }
        /// <summary>
        /// The gateway's figures were still being read when the dashboard stopped waiting (see <see cref="SpeedsMessage"/>):
        /// the page reloads the section shortly to pick them up.
        /// </summary>
        public bool SpeedsPending { get; set; }
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
        /// <summary>
        /// True when the gateway's own figures for this line were read (the main console's gateway, the same cached data
        /// as the Network Overview line cards). The speed fields below are null when unknown, never 0 for "unknown".
        /// </summary>
        public bool Live { get; set; }
        /// <summary>Download right now (Mbps); null when unknown or the line is down.</summary>
        public double? DownMbps { get; set; }
        /// <summary>Upload right now (Mbps); null when unknown or the line is down.</summary>
        public double? UpMbps { get; set; }
        /// <summary>The port's link speed (Mbps, 1000 = 1 Gbps); null when unknown or the line is down.</summary>
        public double? LinkMbps { get; set; }
        /// <summary>The line's latest speed test (gateway status or the 90-day archive); null when none.</summary>
        public NetworkSpeedtestDto? Speedtest { get; set; }
        /// <summary>Data downloaded over the last 24 hours (bytes, from 5-minute traffic); null when unknown.</summary>
        public double? Down24hBytes { get; set; }
        /// <summary>Data uploaded over the last 24 hours (bytes); null when unknown.</summary>
        public double? Up24hBytes { get; set; }
        /// <summary>Traffic over the last 24 hours, 15-minute averages in Mbps (sparkline); empty when unknown.</summary>
        public List<NetworkTrafficPointDto> Traffic { get; set; } = new();
    }

    public sealed class NetworkSpeedtestDto
    {
        public double? DownMbps { get; set; }
        public double? UpMbps { get; set; }
        public double? PingMs { get; set; }
        /// <summary>When it ran (UTC); null when unknown.</summary>
        public string? At { get; set; }
    }

    public sealed class NetworkTrafficPointDto
    {
        /// <summary>Start of the 15-minute step (UTC).</summary>
        public string T { get; set; } = "";
        public double? Down { get; set; }
        public double? Up { get; set; }
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
