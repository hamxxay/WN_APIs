namespace WorkNest.Application.Interfaces
{
    /// <summary>The kinds of item a network alert is about (dbo.WN_NetworkAlertState.Kind).</summary>
    public static class NetworkAlertKinds
    {
        /// <summary>Hikvision access machine (dbo.WN_HIK_Devices).</summary>
        public const string Hik = "hik";
        /// <summary>Internet line of the UniFi gateway.</summary>
        public const string Wan = "wan";
        /// <summary>UniFi access point / switch / gateway.</summary>
        public const string Unifi = "unifi";
        /// <summary>Grandstream (GWN) device.</summary>
        public const string Gwn = "gwn";
    }

    /// <summary>One alert recipient of a location (dbo.WN_NetworkAlertRecipients).</summary>
    public sealed record NetworkAlertRecipient(int Id, int LocationId, string? LocationName, string Email, bool IsActive, DateTime CreatedOn, string? CreatedBy);

    /// <summary>A location with at least one active recipient, with the addresses.</summary>
    public sealed record NetworkAlertLocation(int LocationId, string Name, IReadOnlyList<string> Emails);

    /// <summary>The per-location lease: Claimed = this instance checks the location now; DbNow = the database clock (UTC).</summary>
    public sealed record NetworkAlertClaim(bool Claimed, DateTime DbNow);

    /// <summary>An access machine as the alert check reads it (LastSeen is server time).</summary>
    public sealed record NetworkAlertHikDevice(int Id, string Name, string? Host, bool Online, DateTime? LastSeen);

    /// <summary>What is seen now for one item. Down = the thing the alert is about.</summary>
    public sealed record NetworkAlertItem(string Kind, string Key, string Name, string? Detail, bool Down);

    /// <summary>One item's saved state (UTC times). RecoveredAt null = still down, or recovered without its "back online" e-mail yet.</summary>
    public sealed record NetworkAlertState(string Kind, string ItemKey, string Name, string? Detail, DateTime DownSince,
        DateTime? FirstAlertAt, DateTime? LastReminderAt, DateTime? RecoveredAt);

    /// <summary>One sent alert e-mail. Kind alert | reminder | recovery | test; Status Sent | Held.</summary>
    public sealed record NetworkAlertLogEntry(int LocationId, string Kind, string Subject, string Summary, string Recipients, string Status, string? Error);

    public sealed record NetworkAlertLogRow(int Id, int LocationId, string? LocationName, string Kind, string Subject, string Summary,
        string Recipients, string Status, string? Error, DateTime SentAt);

    /// <summary>
    /// Network alert tables of script NET_ALERT_01_network_alerts. SQL error 208 / 2812 (table / procedure missing) =
    /// the script has not run: <see cref="NetworkAlertsMissingException"/>.
    /// </summary>
    public interface INetworkAlertRepository
    {
        Task<IReadOnlyList<NetworkAlertRecipient>> GetRecipientsAsync(int? locationId = null, CancellationToken ct = default);
        /// <summary>Adds an active recipient. Throws <see cref="NetworkAlertRecipientException"/> for an unknown location or a duplicate.</summary>
        Task<NetworkAlertRecipient> AddRecipientAsync(int locationId, string email, string? createdBy, CancellationToken ct = default);
        /// <summary>False when the recipient does not exist.</summary>
        Task<bool> SetRecipientActiveAsync(int id, bool isActive, CancellationToken ct = default);
        Task<bool> DeleteRecipientAsync(int id, CancellationToken ct = default);
        /// <summary>The locations that have an active recipient (the service's work list).</summary>
        Task<IReadOnlyList<NetworkAlertLocation>> GetActiveLocationsAsync(CancellationToken ct = default);
        /// <summary>Takes the location for one check (see dbo.WN_NetworkAlert_Claim).</summary>
        Task<NetworkAlertClaim> ClaimAsync(int locationId, int leaseSeconds, int minGapSeconds, CancellationToken ct = default);
        Task ReleaseAsync(int locationId, CancellationToken ct = default);
        Task<IReadOnlyList<NetworkAlertHikDevice>> GetHikDevicesAsync(int locationId, CancellationToken ct = default);
        Task<IReadOnlyList<NetworkAlertState>> GetStatesAsync(int locationId, CancellationToken ct = default);
        Task SaveStateAsync(int locationId, NetworkAlertState state, CancellationToken ct = default);
        Task AddLogAsync(NetworkAlertLogEntry entry, CancellationToken ct = default);
        Task<IReadOnlyList<NetworkAlertLogRow>> GetLogAsync(int top, CancellationToken ct = default);
    }

    /// <summary>The database update NET_ALERT_01_network_alerts has not run (SQL errors 208 / 2812).</summary>
    public sealed class NetworkAlertsMissingException : Exception
    {
        /// <summary>What the CMS shows (503). No "sql" in it: the CMS hides messages containing that word.</summary>
        public const string UserMessage = "Database update needed: run the script NET_ALERT_01_network_alerts.";
        public NetworkAlertsMissingException(Exception? inner = null) : base(UserMessage, inner) { }
    }

    /// <summary>A recipient could not be added: the location does not exist, or the e-mail is already in its list.</summary>
    public sealed class NetworkAlertRecipientException : Exception
    {
        public bool Duplicate { get; }
        public NetworkAlertRecipientException(string message, bool duplicate) : base(message) => Duplicate = duplicate;
    }
}
