using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.ServiceSwitch;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// <see cref="IServiceSwitch"/> over dbo.WN_ServiceSwitches (<see cref="IServiceSwitchRepository"/>). All switches are
    /// read in one call and kept in the memory cache for <see cref="CacheFor"/>; turning one on or off clears it, so the
    /// change reaches the services within seconds. Reading fails open (see <see cref="IsEnabledAsync"/>), with a warning in
    /// the log at most once per <see cref="WarnEvery"/>.
    /// </summary>
    public sealed class ServiceSwitchService : IServiceSwitch
    {
        public const string CacheKey = "ServiceSwitches:All";
        public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan WarnEvery = TimeSpan.FromMinutes(1);

        private readonly IServiceSwitchRepository _repository;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ServiceSwitchService> _logger;
        private readonly TimeProvider _time;
        private readonly object _warnLock = new();
        private DateTimeOffset? _lastWarning;
        // Bumped by every SetAsync: a read that started before it does not put its (older) values in the cache.
        private long _version;

        public ServiceSwitchService(IServiceSwitchRepository repository, IMemoryCache cache, IConfiguration configuration,
            ILogger<ServiceSwitchService> logger, TimeProvider? time = null)
        {
            _repository = repository;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;
            _time = time ?? TimeProvider.System;
        }

        public async Task<bool> IsEnabledAsync(string name, CancellationToken ct = default)
        {
            try
            {
                var switches = await GetSwitchesAsync(ct);
                return !switches.TryGetValue(name, out var on) || on; // no row = on
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Table / procedures missing (SS_01 not run) or the database cannot be read: keep the service running.
                WarnThrottled(ex);
                return true;
            }
        }

        public async Task<ServiceSwitchState?> SetAsync(string name, bool enabled, string? user, CancellationToken ct = default)
        {
            var info = BackgroundServiceCatalog.Find(name);
            if (info == null) return null;

            ServiceSwitchRow? row;
            try
            {
                row = await _repository.SetAsync(info.Name, enabled, string.IsNullOrWhiteSpace(user) ? null : user.Trim(), ct);
            }
            finally
            {
                Interlocked.Increment(ref _version);
                _cache.Remove(CacheKey);
            }
            if (row == null) return null; // catalogue name without a row in the table

            _logger.LogInformation("Service switch: {Service} turned {State} by {User}.", info.DisplayName, enabled ? "on" : "off", user ?? "unknown");
            return ToState(info, row);
        }

        public async Task<IReadOnlyList<ServiceSwitchState>> ListAsync(CancellationToken ct = default)
        {
            var version = Interlocked.Read(ref _version);
            var rows = await _repository.GetAllAsync(ct);
            CacheIfCurrent(ToMap(rows), version);
            var byName = rows
                .GroupBy(r => r.ServiceName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            return BackgroundServiceCatalog.All
                .Select(info => ToState(info, byName.TryGetValue(info.Name, out var row) ? row : null))
                .ToList();
        }

        private async Task<IReadOnlyDictionary<string, bool>> GetSwitchesAsync(CancellationToken ct)
        {
            if (_cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, bool>? cached) && cached != null)
                return cached;
            var version = Interlocked.Read(ref _version);
            var map = ToMap(await _repository.GetAllAsync(ct));
            CacheIfCurrent(map, version);
            return map;
        }

        private void CacheIfCurrent(IReadOnlyDictionary<string, bool> map, long versionBeforeRead)
        {
            if (Interlocked.Read(ref _version) == versionBeforeRead)
                _cache.Set(CacheKey, map, CacheFor);
        }

        private static IReadOnlyDictionary<string, bool> ToMap(IEnumerable<ServiceSwitchRow> rows)
        {
            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
                if (!string.IsNullOrWhiteSpace(r.ServiceName)) map[r.ServiceName.Trim()] = r.IsEnabled;
            return map;
        }

        private ServiceSwitchState ToState(BackgroundServiceInfo info, ServiceSwitchRow? row) => new()
        {
            Name = info.Name,
            DisplayName = info.DisplayName,
            Description = info.Description,
            IsEnabled = row?.IsEnabled ?? true,
            ConfigKey = info.ConfigKey,
            ConfigEnabled = info.IsConfigEnabled(_configuration),
            UpdatedBy = row?.UpdatedBy,
            UpdatedAt = row?.UpdatedAt
        };

        private void WarnThrottled(Exception ex)
        {
            var now = _time.GetUtcNow();
            lock (_warnLock)
            {
                if (_lastWarning is { } last && now - last < WarnEvery) return;
                _lastWarning = now;
            }
            if (ex is ServiceSwitchesMissingException)
                _logger.LogWarning("Service switches are not on this database yet (script SS_01_service_switches); every background service runs as if switched on.");
            else
                _logger.LogWarning(ex, "Could not read the service switches; every background service runs as if switched on.");
        }
    }
}
