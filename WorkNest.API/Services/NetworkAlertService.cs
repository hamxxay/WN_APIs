using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Network outage e-mail alerts (NET_ALERT_01): every NetworkAlerts:TickSeconds (default 60) runs
    /// <see cref="NetworkAlertProcessor"/>: a location's recipients get one e-mail when an access machine, an internet line
    /// or a network device has been down for NetworkAlerts:DownMinutes (5), a reminder every NetworkAlerts:ReminderHours
    /// (6) while it stays down, and one "back online" e-mail. On / off with the NetworkAlerts service switch (Services
    /// page) and "NetworkAlerts:Enabled": false. Before NET_ALERT_01 it logs once and idles (checks again each tick).
    /// </summary>
    public sealed class NetworkAlertService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<NetworkAlertService> _logger;
        private readonly ServiceSwitchGate _gate;
        private readonly bool _enabled;
        private readonly TimeSpan _tick;
        private readonly TimeSpan _startupDelay;
        private bool _warnedMissing;

        public NetworkAlertService(IServiceScopeFactory scopes, IConfiguration configuration, IServiceSwitch serviceSwitch,
            ILogger<NetworkAlertService> logger)
            : this(scopes, configuration, serviceSwitch, logger, TimeSpan.FromSeconds(45)) { }

        /// <summary>For tests: a chosen start-up delay.</summary>
        public NetworkAlertService(IServiceScopeFactory scopes, IConfiguration configuration, IServiceSwitch serviceSwitch,
            ILogger<NetworkAlertService> logger, TimeSpan startupDelay)
        {
            _scopes = scopes;
            _logger = logger;
            _gate = new ServiceSwitchGate(serviceSwitch, BackgroundServiceCatalog.NetworkAlerts, logger);
            _enabled = BackgroundServiceCatalog.Get(BackgroundServiceCatalog.NetworkAlerts).IsConfigEnabled(configuration);
            _tick = TimeSpan.FromSeconds(int.TryParse(configuration["NetworkAlerts:TickSeconds"], out var s) && s > 0 ? s : NetworkAlertProcessor.DefaultTickSeconds);
            _startupDelay = startupDelay;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("WorkNest Network Alert Service is disabled (NetworkAlerts:Enabled = false).");
                return;
            }
            _logger.LogInformation("WorkNest Network Alert Service started (every {Seconds}s).", _tick.TotalSeconds);
            // After start-up the UniFi / GWN pollers need a moment to have figures; until then their kinds are simply not read.
            try { await Task.Delay(_startupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

            using var timer = new PeriodicTimer(_tick);
            try
            {
                do { await TickAsync(stoppingToken); }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            _logger.LogInformation("WorkNest Network Alert Service stopping.");
        }

        /// <summary>One tick: the switch, then the processor in its own scope. Never throws (except on shutdown).</summary>
        public async Task<NetworkAlertProcessor.TickResult?> TickAsync(CancellationToken ct)
        {
            try
            {
                if (!await _gate.IsOnAsync(ct)) return null;
                using var scope = _scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<NetworkAlertProcessor>().RunOnceAsync(ct);
                _warnedMissing = false;
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (NetworkAlertsMissingException)
            {
                if (!_warnedMissing) _logger.LogWarning("Network alerts idle: {Message}", NetworkAlertsMissingException.UserMessage);
                _warnedMissing = true;
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during the network alert check.");
                return null;
            }
        }
    }
}
