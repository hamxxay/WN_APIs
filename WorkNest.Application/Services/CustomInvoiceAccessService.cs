using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.CustomInvoice;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// <see cref="ICustomInvoiceAccessService"/>: a super admin may always use custom invoices; an admin only with the
    /// <see cref="Permission"/> permission (dbo.WN_UserPermissions_GetByUser), kept in the memory cache for
    /// <see cref="CacheFor"/> per user and dropped by <see cref="Forget"/> when a super admin changes it. Before CI_01
    /// (procedure missing) and on any database failure an admin is refused; a super admin never needs the database.
    /// </summary>
    public sealed class CustomInvoiceAccessService : ICustomInvoiceAccessService
    {
        /// <summary>The permission name in dbo.WN_UserPermissions.</summary>
        public const string Permission = "CustomInvoice";
        public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
        /// <summary>E-mail -> WN_Users.Id; an account's id never changes, so this one is kept longer.</summary>
        private static readonly TimeSpan UserIdCacheFor = TimeSpan.FromMinutes(5);

        private readonly ICustomInvoiceRepository _repository;
        private readonly IDbRepository _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CustomInvoiceAccessService> _logger;

        public CustomInvoiceAccessService(ICustomInvoiceRepository repository, IDbRepository db, IMemoryCache cache,
            ILogger<CustomInvoiceAccessService> logger)
        {
            _repository = repository;
            _db = db;
            _cache = cache;
            _logger = logger;
        }

        public static string PermissionCacheKey(int userId) => $"CustomInvoiceAccess:Permission:{userId}";
        private static string UserIdCacheKey(string email) => $"CustomInvoiceAccess:UserId:{email.Trim().ToLowerInvariant()}";

        public async Task<CustomInvoiceAccessMe> CheckAsync(CustomInvoiceCaller caller)
        {
            if (caller.IsSuperAdmin) return new CustomInvoiceAccessMe { CanUse = true, IsSuperAdmin = true };
            if (!caller.IsAdmin) return new CustomInvoiceAccessMe();

            try
            {
                var userId = await ResolveUserIdAsync(caller.Email);
                return new CustomInvoiceAccessMe { CanUse = userId is > 0 && await HasPermissionAsync(userId.Value) };
            }
            catch (Exception ex)
            {
                // The database cannot be read: refuse (not cached, so the next request asks again).
                _logger.LogWarning(ex, "Custom invoice access could not be checked for {Email}; refused.", caller.Email);
                return new CustomInvoiceAccessMe();
            }
        }

        public async Task<int?> ResolveUserIdAsync(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            var key = UserIdCacheKey(email);
            if (_cache.TryGetValue(key, out int cached) && cached > 0) return cached;

            var (id, _) = await _db.GetUserIdByEmailAsync(email.Trim());
            if (id is not > 0) return null;
            _cache.Set(key, id.Value, UserIdCacheFor);
            return id;
        }

        public void Forget(int userId) => _cache.Remove(PermissionCacheKey(userId));

        private async Task<bool> HasPermissionAsync(int userId)
        {
            var key = PermissionCacheKey(userId);
            if (_cache.TryGetValue(key, out bool cached)) return cached;

            bool has;
            try
            {
                var permissions = await _repository.GetUserPermissionsAsync(userId);
                has = permissions.Any(p => string.Equals(p?.Trim(), Permission, StringComparison.OrdinalIgnoreCase));
            }
            catch (CustomInvoiceDatabaseMissingException)
            {
                return false; // CI_01 not run: no admin has the permission yet (not cached)
            }
            _cache.Set(key, has, CacheFor);
            return has;
        }
    }
}
