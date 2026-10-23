using System.Text.Json;

namespace WorkNest.Application.DTOs.Gwn
{
    // Grandstream GWN network (api/gwn/*), contract Database/Gwn/GWN_CONTRACT.md. Serialized camelCase; times UTC ISO 8601
    // with Z; mac "AA:BB:CC:DD:EE:FF"; bytes as numbers (null = unknown). Never carries a secret.

    /// <summary>A GWN network and the WorkNest locations it belongs to (dbo.WN_GWN_NetworkLocations).</summary>
    public sealed class GwnNetworkDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public List<int> LocationIds { get; set; } = new();
    }

    /// <summary>GET api/gwn/network-locations</summary>
    public sealed class GwnNetworkLocationsDto
    {
        /// <summary>False before GWN_01_grandstream has run (no network is linked to a location then).</summary>
        public bool Available { get; set; } = true;
        public string? Message { get; set; }
        public bool CanEdit { get; set; }
        public List<GwnNetworkDto> Networks { get; set; } = new();
    }

    /// <summary>Every endpoint while GWN is not set up (or turned off) on this server.</summary>
    public sealed class GwnUnavailableDto
    {
        public bool Available { get; set; }
        public string Message { get; set; } = "";
    }

    public sealed class GwnUsageDto
    {
        public double? Download { get; set; }
        public double? Upload { get; set; }
        public double? Total { get; set; }
    }

    public class GwnDeviceDto
    {
        public string Mac { get; set; } = "";
        public string Name { get; set; } = "";
        public string Model { get; set; } = "";
        /// <summary>Router | Access point</summary>
        public string Type { get; set; } = "";
        /// <summary>online | offline</summary>
        public string Status { get; set; } = "";
        /// <summary>LAN address (routers report their WAN address separately in <see cref="PublicIp"/>).</summary>
        public string Ip { get; set; } = "";
        public string PublicIp { get; set; } = "";
        public string Ipv6 { get; set; } = "";
        public string Firmware { get; set; } = "";
        /// <summary>A newer firmware GWN offers ("" = up to date / unknown).</summary>
        public string LatestFirmware { get; set; } = "";
        public double? UptimeSeconds { get; set; }
        public int Clients { get; set; }
        /// <summary>Since the device was added to GWN (bytes).</summary>
        public double? Download { get; set; }
        public double? Upload { get; set; }
        public double? Total { get; set; }
        /// <summary>Data through the device today (network time zone), from the 1-day bandwidth statistics.</summary>
        public GwnUsageDto? Today { get; set; }
        public string Temperature { get; set; } = "";
        public string Memory { get; set; } = "";
        public int? Channel24 { get; set; }
        public int? Channel5 { get; set; }
        public string Remarks { get; set; } = "";
        public string? FirstSeen { get; set; }
        public string? LastSeen { get; set; }

        public GwnDeviceDto Copy() => (GwnDeviceDto)MemberwiseClone();
    }

    public sealed class GwnInfoRowDto
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public sealed class GwnRadioDto
    {
        public string Band { get; set; } = "";
        public double? Channel { get; set; }
        public double? Clients { get; set; }
        public string Power { get; set; } = "";
    }

    /// <summary>GET api/gwn/device</summary>
    public sealed class GwnDeviceDetailDto : GwnDeviceDto
    {
        public List<GwnInfoRowDto> Info { get; set; } = new();
        public List<GwnRadioDto> Radios { get; set; } = new();
        /// <summary>Why <see cref="Info"/> is empty (GWN did not answer ap/info); null when it was read.</summary>
        public string? InfoError { get; set; }
        public List<GwnClientDto> ClientList { get; set; } = new();
    }

    public class GwnClientDto
    {
        public string Mac { get; set; } = "";
        /// <summary>Alias, else GWN name, else host name, else the MAC.</summary>
        public string Name { get; set; } = "";
        /// <summary>The WorkNest name (dbo.WN_GWN_ClientAliases); "" when none.</summary>
        public string Alias { get; set; } = "";
        /// <summary>The name GWN keeps for it ("" when none).</summary>
        public string GwnName { get; set; } = "";
        public string Ip { get; set; } = "";
        public string Ipv6 { get; set; } = "";
        public string Ssid { get; set; } = "";
        public string SsidId { get; set; } = "";
        public string ApMac { get; set; } = "";
        public string ApName { get; set; } = "";
        /// <summary>2.4G | 5G | 6G | Wired | ""</summary>
        public string Band { get; set; } = "";
        public double? Channel { get; set; }
        public double? Rssi { get; set; }
        /// <summary>As GWN words it ("1d 16h33m").</summary>
        public string ConnectedFor { get; set; } = "";
        public double? ConnectedSeconds { get; set; }
        /// <summary>This session (bytes; GWN rxBytes = the client's download).</summary>
        public double Download { get; set; }
        public double Upload { get; set; }
        public double Total { get; set; }
        public bool Online { get; set; }
        public bool Wired { get; set; }
        public bool Guest { get; set; }
        public bool Blocked { get; set; }
        public string Manufacturer { get; set; } = "";
        public string Os { get; set; } = "";
        public string Host { get; set; } = "";
        public string Vlan { get; set; } = "";
        public string? FirstSeen { get; set; }
        public string? LastSeen { get; set; }

        public GwnClientDto Copy() => (GwnClientDto)MemberwiseClone();
    }

    /// <summary>GET api/gwn/client</summary>
    public sealed class GwnClientDetailDto : GwnClientDto
    {
        /// <summary>Last activity GWN recorded (UTC); null when unknown.</summary>
        public string? LastActive { get; set; }
        /// <summary>GWN's client/info answer as it came ({} when it could not be read).</summary>
        public JsonElement Raw { get; set; }
        public string? InfoError { get; set; }
    }

    public sealed class GwnSsidConfigDto
    {
        public List<string> Bands { get; set; } = new();
        public bool Hidden { get; set; }
        public double? ClientLimit { get; set; }
        public double? VlanId { get; set; }
        public bool Roaming80211r { get; set; }
        public bool Roaming80211k { get; set; }
        public bool Roaming80211v { get; set; }
        public double? IdleTimeout { get; set; }
        public bool Mlo { get; set; }
    }

    public sealed class GwnSsidDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Remark { get; set; } = "";
        public bool Enabled { get; set; }
        /// <summary>Clients connected to it right now.</summary>
        public int Clients { get; set; }
        /// <summary>Online GWN devices broadcasting it.</summary>
        public int Devices { get; set; }
        public string Security { get; set; } = "";
        public bool Schedule { get; set; }
        public bool Portal { get; set; }
        public bool Isolation { get; set; }
        public double? Vlan { get; set; }
        /// <summary>Off | Whitelist | Blacklist</summary>
        public string MacFiltering { get; set; } = "Off";
        /// <summary>Connected clients per band ("2.4G", "5G", "6G", "Wired", "Other"); only on api/gwn/ssids.</summary>
        public Dictionary<string, int>? Bands { get; set; }
        public int? Weak { get; set; }
        public double? Download { get; set; }
        public double? Upload { get; set; }
        /// <summary>Non-secret settings (never keys or passphrases); only on api/gwn/ssids.</summary>
        public GwnSsidConfigDto? Config { get; set; }

        public GwnSsidDto Copy() => (GwnSsidDto)MemberwiseClone();
    }

    public sealed class GwnAlertDto
    {
        /// <summary>critical | serious | warning</summary>
        public string Level { get; set; } = "";
        public string Text { get; set; } = "";
        public string Where { get; set; } = "";
    }

    public sealed class GwnOverviewDto
    {
        public bool Available { get; set; } = true;
        public string? Message { get; set; }
        public string? UpdatedAt { get; set; }
        public GwnOverviewNetworkDto? Network { get; set; }
        public GwnOverviewDevicesDto? Devices { get; set; }
        public GwnClientCountsDto? Clients { get; set; }
        public GwnOverviewUsageDto? Usage { get; set; }
        public GwnTrafficNowDto? TrafficNow { get; set; }
        public List<GwnSsidDto> Ssids { get; set; } = new();
        public List<GwnAlertDto> Alerts { get; set; } = new();
    }

    public sealed class GwnOverviewNetworkDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Country { get; set; } = "";
        /// <summary>IANA zone, e.g. Asia/Karachi.</summary>
        public string Timezone { get; set; } = "UTC";
        public string TimezoneDisplay { get; set; } = "";
        public List<int> LocationIds { get; set; } = new();
    }

    public sealed class GwnOverviewDevicesDto
    {
        public int Total { get; set; }
        public int Online { get; set; }
        public int Offline { get; set; }
        public List<GwnDeviceDto> List { get; set; } = new();
    }

    public sealed class GwnClientCountsDto
    {
        public int Total { get; set; }
        public int Band24 { get; set; }
        public int Band5 { get; set; }
        public int Band6 { get; set; }
        public int Wired { get; set; }
        /// <summary>Wi-Fi clients with a signal below -75 dBm.</summary>
        public int Weak { get; set; }
        /// <summary>The sum of the devices' own client counts (can differ from the client list).</summary>
        public int DeviceReported { get; set; }
    }

    public sealed class GwnOverviewUsageDto
    {
        /// <summary>Midnight in the network's time zone (UTC).</summary>
        public string? Since { get; set; }
        public double? Download { get; set; }
        public double? Upload { get; set; }
        public double? Total { get; set; }
    }

    public sealed class GwnTrafficNowDto
    {
        /// <summary>Last 5-minute average (bits per second); null when unknown.</summary>
        public double? DownloadBps { get; set; }
        public double? UploadBps { get; set; }
    }

    /// <summary>api/gwn/stats/clients</summary>
    public sealed class GwnCountSeriesDto
    {
        public List<string?> Times { get; set; } = new();
        public List<double?> Counts { get; set; } = new();
    }

    /// <summary>api/gwn/stats/bandwidth, api/gwn/client/usage: bytes per step and bits per second.</summary>
    public sealed class GwnBandwidthDto
    {
        public List<string?> Times { get; set; } = new();
        public List<double?> Download { get; set; } = new();
        public List<double?> Upload { get; set; } = new();
        public List<double?> DownloadBps { get; set; } = new();
        public List<double?> UploadBps { get; set; } = new();
        public double? DownloadTotal { get; set; }
        public double? UploadTotal { get; set; }
    }

    /// <summary>api/gwn/device/stats</summary>
    public sealed class GwnDeviceStatsDto
    {
        public List<string?> Times { get; set; } = new();
        public List<double?> Clients { get; set; } = new();
        public List<double?> Download { get; set; } = new();
        public List<double?> Upload { get; set; } = new();
        public List<double?> DownloadBps { get; set; } = new();
        public List<double?> UploadBps { get; set; } = new();
        public double? DownloadTotal { get; set; }
        public double? UploadTotal { get; set; }
    }

    public sealed class GwnSsidSeriesDto
    {
        public string Name { get; set; } = "";
        public List<double?> Counts { get; set; } = new();
    }

    /// <summary>api/gwn/stats/ssid</summary>
    public sealed class GwnSsidStatsDto
    {
        public List<string?> Times { get; set; } = new();
        public List<double?> Counts { get; set; } = new();
        public List<GwnSsidSeriesDto> Series { get; set; } = new();
    }

    /// <summary>POST api/gwn/alias</summary>
    public sealed class GwnAliasResultDto
    {
        public string Mac { get; set; } = "";
        public string Alias { get; set; } = "";
    }

    /// <summary>POST api/gwn/client/block</summary>
    public sealed class GwnBlockResultDto
    {
        public string Mac { get; set; } = "";
        public bool Blocked { get; set; }
    }

    /// <summary>POST api/gwn/device/reboot</summary>
    public sealed class GwnRebootResultDto
    {
        public string Mac { get; set; } = "";
        public bool Rebooting { get; set; }
    }

    /// <summary>One row of dbo.WN_GWN_ActionLog (api/gwn/actions).</summary>
    public sealed class GwnActionLogDto
    {
        public long Id { get; set; }
        public long NetworkId { get; set; }
        /// <summary>block | unblock | reboot</summary>
        public string Action { get; set; } = "";
        public string Target { get; set; } = "";
        public string? ByEmail { get; set; }
        public int? ByUserId { get; set; }
        public bool Ok { get; set; }
        public string? Message { get; set; }
        public string? CreatedAt { get; set; }
    }

    /// <summary>What a block / unblock / reboot writes to dbo.WN_GWN_ActionLog.</summary>
    public sealed record GwnActionLogEntry(long NetworkId, string Action, string Target, string? ByEmail, int? ByUserId, bool Ok, string? Message);
}
