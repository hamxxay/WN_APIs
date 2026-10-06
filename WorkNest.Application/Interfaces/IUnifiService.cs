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
    }
}
