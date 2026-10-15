namespace WorkNest.Application.Services
{
    /// <summary>
    /// The WhatsApp bot's last good list of active branches (dbo.WN_Locations rows), shared by every request (registered
    /// as a singleton): while it is fresh no message reads the database, and when WN_Locations later fails or is slow the
    /// bot keeps using it instead of a fallback.
    /// </summary>
    public sealed class WhatsAppBranchCache
    {
        /// <summary>How long a loaded list is used before WN_Locations is read again.</summary>
        public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>How long a reply waits for WN_Locations before it uses the last good list or a fallback.</summary>
        public TimeSpan LoadTimeout { get; set; } = TimeSpan.FromSeconds(3);

        private sealed record Snapshot(IReadOnlyList<IDictionary<string, object?>> Rows, DateTime LoadedAtUtc);

        private volatile Snapshot? _last;

        /// <summary>The rows of the last successful load (any age), or null when WN_Locations was never loaded.</summary>
        public IReadOnlyList<IDictionary<string, object?>>? LastGood => _last?.Rows;

        /// <summary>When <see cref="LastGood"/> was loaded (UTC).</summary>
        public DateTime? LoadedAtUtc => _last?.LoadedAtUtc;

        /// <summary>True while the last good list is younger than <see cref="Lifetime"/>.</summary>
        public bool IsFresh => _last is { } s && DateTime.UtcNow - s.LoadedAtUtc < Lifetime;

        public void Store(IReadOnlyList<IDictionary<string, object?>> rows) => _last = new Snapshot(rows, DateTime.UtcNow);
    }
}
