using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.DTOs.Gwn;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Grandstream GWN network (WorkNest F-7) — port of WN_Grandstream/server.js. Singleton: caches what it reads from GWN
    /// (stale-while-revalidate), kept warm by GwnPollingService. Which networks a caller may see is decided by the caller
    /// (GwnController / NetworkDashboardService) with <see cref="WorkNest.Application.Helpers.GwnNetworkScope"/>.
    /// GWN failures throw <see cref="GwnApiException"/> (safe message).
    /// </summary>
    public interface IGwnService
    {
        /// <summary>Gwn:AppId / SecretKey / Username / Password are set.</summary>
        bool IsConfigured { get; }
        /// <summary>Gwn:Enabled is not "false".</summary>
        bool IsEnabled { get; }
        /// <summary>Null when GWN can be used; else the plain sentence every endpoint answers (not set up / turned off).</summary>
        string? UnavailableMessage { get; }
        int RefreshSeconds { get; }

        // --- Polling (GwnPollingService) ---
        /// <summary>Networks, devices, clients, SSIDs and device details of every network, read again now.</summary>
        Task RefreshCoreAsync(CancellationToken ct = default);
        /// <summary>Statistics (clients, bandwidth, per device today) of every network, read again now.</summary>
        Task RefreshStatsAsync(CancellationToken ct = default);

        // --- Networks and locations ---
        /// <summary>
        /// Every GWN network of the account (plus networks only known from dbo.WN_GWN_NetworkLocations while GWN cannot be
        /// reached), each with its WorkNest locations.
        /// </summary>
        Task<IReadOnlyList<GwnNetworkDto>> GetNetworksAsync(CancellationToken ct = default);
        Task<GwnNetworkLocationsDto> GetNetworkLocationsAsync(bool canEdit, CancellationToken ct = default);
        /// <summary>Saves the locations of one network; null when the network is not in the GWN account.</summary>
        Task<GwnNetworkDto?> SetNetworkLocationsAsync(long networkId, IReadOnlyList<int> locationIds, string? byEmail, CancellationToken ct = default);

        // --- Pages ---
        Task<GwnOverviewDto> GetOverviewAsync(GwnNetworkDto network, CancellationToken ct = default);
        Task<List<GwnClientDto>> GetClientsAsync(long networkId, CancellationToken ct = default);
        /// <summary>Clients seen between from and to (UTC; null = open), most recent first.</summary>
        Task<List<GwnClientDto>> GetClientHistoryAsync(long networkId, DateTime? from, DateTime? to, CancellationToken ct = default);
        /// <summary>One client; null when GWN does not know it.</summary>
        Task<GwnClientDetailDto?> GetClientAsync(long networkId, string mac, CancellationToken ct = default);
        Task<GwnBandwidthDto> GetClientUsageAsync(long networkId, string mac, string? range, CancellationToken ct = default);
        Task<List<GwnDeviceDto>> GetDevicesAsync(long networkId, CancellationToken ct = default);
        /// <summary>One device; null when it is not in this network.</summary>
        Task<GwnDeviceDetailDto?> GetDeviceAsync(long networkId, string mac, CancellationToken ct = default);
        Task<GwnDeviceStatsDto> GetDeviceStatsAsync(long networkId, string mac, string? range, CancellationToken ct = default);
        Task<List<GwnSsidDto>> GetSsidsAsync(long networkId, CancellationToken ct = default);
        Task<GwnCountSeriesDto> GetClientStatsAsync(long networkId, string? range, CancellationToken ct = default);
        Task<GwnBandwidthDto> GetBandwidthStatsAsync(long networkId, string? range, CancellationToken ct = default);
        Task<GwnSsidStatsDto> GetSsidStatsAsync(long networkId, string? range, string? ssidId, CancellationToken ct = default);

        // --- Changes ---
        /// <summary>Renames a client (empty name removes the alias). Null when the MAC is invalid.</summary>
        Task<GwnAliasResultDto?> SetAliasAsync(string? mac, string? name, string? byEmail, CancellationToken ct = default);
        /// <summary>Blocks / unblocks a client on GWN (client/block); logged in dbo.WN_GWN_ActionLog, failed or not.</summary>
        Task<GwnBlockResultDto> BlockClientAsync(long networkId, string mac, bool block, string? byEmail, int? byUserId, CancellationToken ct = default);
        /// <summary>Reboots a device of the network (ap/reboot); null when it is not in the network. Logged.</summary>
        Task<GwnRebootResultDto?> RebootDeviceAsync(long networkId, string mac, string? byEmail, int? byUserId, CancellationToken ct = default);
        Task<List<GwnActionLogDto>> GetActionsAsync(long? networkId, int top, CancellationToken ct = default);

        // --- CEO / Sales Manager dashboards ---
        /// <summary>The Gwn part of the Network section for these networks (never throws for GWN trouble).</summary>
        Task<GwnDashboardSectionDto> GetDashboardSectionAsync(IReadOnlyList<GwnNetworkDto> networks, CancellationToken ct = default);
    }

    /// <summary>
    /// dbo.WN_GWN_NetworkLocations, dbo.WN_GWN_ClientAliases, dbo.WN_GWN_ActionLog (script GWN_01_grandstream). SQL error
    /// 208 (table missing) = GWN_01 not run: reads throw <see cref="GwnTablesMissingException"/>.
    /// </summary>
    public interface IGwnRepository
    {
        Task<IReadOnlyList<(long NetworkId, int LocationId, string? NetworkName)>> GetNetworkLocationsAsync(CancellationToken ct = default);
        /// <summary>Replaces the network's locations (empty = linked to none).</summary>
        Task SetNetworkLocationsAsync(long networkId, string? networkName, IReadOnlyCollection<int> locationIds, string? updatedBy, CancellationToken ct = default);
        /// <summary>Aliases keyed by MAC "AA:BB:CC:DD:EE:FF".</summary>
        Task<Dictionary<string, string>> GetAliasesAsync(CancellationToken ct = default);
        Task SetAliasAsync(string mac, string alias, string? updatedBy, CancellationToken ct = default);
        Task DeleteAliasAsync(string mac, CancellationToken ct = default);
        Task AddActionAsync(GwnActionLogEntry entry, CancellationToken ct = default);
        Task<List<GwnActionLogDto>> GetActionsAsync(long? networkId, int top, CancellationToken ct = default);
    }

    /// <summary>The GWN tables are missing: GWN_01_grandstream has not run on this database.</summary>
    public class GwnTablesMissingException : InvalidOperationException
    {
        public const string UserMessage = "The Grandstream tables are not set up yet. Run the database update GWN_01_grandstream.";
        public GwnTablesMissingException(Exception? inner = null) : base(UserMessage, inner) { }
    }
}
