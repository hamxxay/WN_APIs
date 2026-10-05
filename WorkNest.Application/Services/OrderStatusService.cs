using WorkNest.Application.DTOs.OrderStatus;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Reads dbo.OrderStatus (active rows) once and caches it for 10 minutes, so status IDs are looked up
    /// by description instead of being hard-coded.
    /// </summary>
    public class OrderStatusService : IOrderStatusService
    {
        private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
        private static readonly SemaphoreSlim Lock = new(1, 1);
        private static Dictionary<string, int>? _byName;
        private static DateTime _loadedAt;

        // Legacy invoice / payment values still present on older rows.
        private const int LegacyUnpaid = 1, LegacyPaid = 2, LegacyPartial = 3, LegacyOverdue = 4, LegacyVoid = 5;

        private readonly IDbRepository _db;

        public OrderStatusService(IDbRepository db)
        {
            _db = db;
        }

        public async Task<int?> GetIdAsync(string description)
        {
            var map = await LoadAsync();
            return map.TryGetValue(Normalize(description), out var id) ? id : null;
        }

        public async Task<InvoiceStatuses> GetInvoiceStatusesAsync()
        {
            var unpaid = await GetIdAsync(InvoiceStatuses.UnpaidName);
            var paid = await GetIdAsync(InvoiceStatuses.PaidName);
            var partial = await GetIdAsync(InvoiceStatuses.PartialName);
            var overdue = await GetIdAsync(InvoiceStatuses.OverdueName);
            var cancelled = await GetIdAsync(InvoiceStatuses.CancelledName);

            static HashSet<int> Set(int legacy, int? current) =>
                current.HasValue ? new HashSet<int> { legacy, current.Value } : new HashSet<int> { legacy };

            return new InvoiceStatuses
            {
                UnpaidId = unpaid, PaidId = paid, PartialId = partial, OverdueId = overdue, CancelledId = cancelled,
                Unpaid = Set(LegacyUnpaid, unpaid),
                Paid = Set(LegacyPaid, paid),
                Partial = Set(LegacyPartial, partial),
                Overdue = Set(LegacyOverdue, overdue),
                Cancelled = Set(LegacyVoid, cancelled)
            };
        }

        private async Task<Dictionary<string, int>> LoadAsync()
        {
            if (_byName != null && DateTime.UtcNow - _loadedAt < CacheFor) return _byName;
            await Lock.WaitAsync();
            try
            {
                if (_byName != null && DateTime.UtcNow - _loadedAt < CacheFor) return _byName;
                var rows = await _db.GetOrderStatusesDbAsync();
                var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var (id, description) in rows.OrderBy(r => r.Key))
                {
                    var key = Normalize(description);
                    if (key.Length > 0 && !map.ContainsKey(key)) map[key] = id; // first (lowest) ID wins on duplicates
                }
                _byName = map;
                _loadedAt = DateTime.UtcNow;
                return map;
            }
            finally
            {
                Lock.Release();
            }
        }

        private static string Normalize(string? s) => (s ?? "").Replace("\r", "").Replace("\n", "").Trim();
    }
}
