using System.Text.Json.Nodes;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// UniFi network dashboard (port of "Unifi UI" server.js): polls the Site Manager cloud API and the
    /// console Network API, keeps state in memory (singleton) and builds the dashboard payloads.
    /// Every payload keeps the exact JSON field names of the Node server so the dashboard pages work unchanged.
    /// </summary>
    public interface IUnifiService
    {
        bool IsConfigured { get; }
        int RefreshSeconds { get; }

        // --- Polling (driven by UnifiPollingService) ---
        /// <summary>Load client-count history and client aliases from the database (once; safe to call repeatedly).</summary>
        Task InitializeAsync(CancellationToken ct = default);
        /// <summary>Hosts, sites and devices from the cloud API, then a history sample.</summary>
        Task RefreshCoreAsync(CancellationToken ct = default);
        /// <summary>ISP metrics (5-minute buckets, last 24h).</summary>
        Task RefreshIspAsync(CancellationToken ct = default);
        /// <summary>Keep the console event log cached so client/device history opens instantly.</summary>
        Task WarmLogsAsync();

        // --- Dashboard payloads ---
        Task<JsonObject> GetSummaryAsync();
        Task<JsonObject> RefreshAsync();
        Task<JsonObject> GetClientsAsync();
        Task<JsonObject> GetTopologyAsync();
        Task<JsonObject> GetWansAsync();
        Task<JsonObject> GetIspAsync();
        Task<JsonObject> GetWifiAsync();
        Task<JsonObject> GetClientUsageAsync(string? mac);
        Task<JsonObject> GetTopUsageAsync(int days);
        Task<JsonObject> StartSpeedtestAsync(string? wan);
        Task<JsonObject> GetSpeedtestStatusAsync();
        Task<JsonObject> GetWanTrafficAsync(int rangeHours);
        Task<JsonObject> GetConfigAsync();
        /// <summary>Rename a client (empty name removes the alias). Null when the MAC is invalid.</summary>
        Task<JsonObject?> SetAliasAsync(string? mac, string? name, string? updatedByEmail);
        Task<JsonObject> GetLogsAsync(string? mac);
        /// <summary>Device detail, its clients and logs; null when the device is not on the console.</summary>
        Task<JsonObject?> GetDeviceAsync(string mac);
        /// <summary>Traffic / clients / CPU history of one device; null when the device is not on the console.</summary>
        Task<JsonObject?> GetDeviceHistoryAsync(string mac, int rangeHours);

        // --- Wi-Fi SSIDs and per-SSID MAC filters (allow-list / block-list) ---
        /// <summary>Every SSID (WLAN) with its MAC filter and the clients connected to it right now.</summary>
        Task<JsonObject> GetSsidsAsync();
        /// <summary>
        /// Changes one SSID's MAC filter. action: add | remove (a MAC in the list), block | unblock (keep a device off /
        /// let it back on, whatever the mode), mode (policy = allow | deny | off; macs = MACs to add to the list in the same step). Returns the updated SSID, or
        /// { error } for a bad request. Every change is written to WN_UNIFI_MacFilterLog.
        /// </summary>
        Task<JsonObject> UpdateMacFilterAsync(string wlanId, string action, string? mac, string? policy, string? reason, string? byEmail, int? byUserId, IReadOnlyList<string>? macs = null, string? deviceName = null, string? roomNo = null);
        /// <summary>Shows or hides the SSID name (UniFi hide_ssid). Logged like MAC filter changes. Returns the SSID or { error }.</summary>
        Task<JsonObject> SetSsidHiddenAsync(string wlanId, bool hidden, string? reason, string? byEmail, int? byUserId);
        /// <summary>UniFi speed profiles (user groups, except Default) with their per-device speeds and the SSIDs using them.</summary>
        Task<JsonObject> GetSpeedProfilesAsync();
        /// <summary>Creates a UniFi speed profile (Mbps; null = no limit that way). Returns the profile or { error }.</summary>
        Task<JsonObject> CreateSpeedProfileAsync(string? name, int? downMbps, int? upMbps);
        /// <summary>Puts the SSID on a speed profile (null = Default, no limit). Logged. Returns the SSID or { error }.</summary>
        Task<JsonObject> SetSsidSpeedProfileAsync(string wlanId, string? profileId, string? reason, string? byEmail, int? byUserId);
        /// <summary>WN_Locations the UniFi network belongs to (WN_UNIFI_Settings "LocationIds"); empty = not set (everyone sees it).</summary>
        Task<IReadOnlyList<int>> GetLocationIdsAsync();
        /// <summary>Saves the locations the network belongs to; false when the settings table isn't created yet.</summary>
        Task<bool> SetLocationIdsAsync(IReadOnlyList<int> locationIds, string? byEmail);
        /// <summary>Latest MAC filter changes, optionally for one SSID or one MAC (max 200).</summary>
        Task<JsonObject> GetMacFilterLogAsync(string? wlanId, string? mac);
    }
}
