using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Keeps the Grandstream (GWN) cache warm, like warm() in WN_Grandstream/server.js: networks, devices, clients, SSIDs
    /// and device details every Gwn:RefreshSeconds (default 60), statistics (clients, bandwidth, per-device today) every
    /// 5 minutes (GWN updates them every 5 minutes). On / off with the GwnPolling service switch (Services page); does
    /// nothing without the Gwn settings or with "Gwn:Enabled": false.
    /// </summary>
    public sealed class GwnPollingService : BackgroundService
    {
        private static readonly TimeSpan StatsInterval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _startupDelay;

        private readonly ILogger<GwnPollingService> _logger;
        private readonly IGwnService _gwn;
        private readonly bool _enabled;
        private readonly ServiceSwitchGate _gate;

        public GwnPollingService(ILogger<GwnPollingService> logger, IGwnService gwn, IConfiguration configuration, IServiceSwitch serviceSwitch)
            : this(logger, gwn, configuration, serviceSwitch, TimeSpan.FromSeconds(8)) { }

        /// <summary>For tests: no start-up delay.</summary>
        public GwnPollingService(ILogger<GwnPollingService> logger, IGwnService gwn, IConfiguration configuration, IServiceSwitch serviceSwitch, TimeSpan startupDelay)
        {
            _logger = logger;
            _gwn = gwn;
            _gate = new ServiceSwitchGate(serviceSwitch, BackgroundServiceCatalog.GwnPolling, logger);
            _enabled = !string.Equals(configuration["Gwn:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
            _startupDelay = startupDelay;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("WorkNest Grandstream Polling Service is disabled (Gwn:Enabled = false).");
                return;
            }
            if (!_gwn.IsConfigured)
            {
                _logger.LogWarning("WorkNest Grandstream Polling Service not started: the Gwn settings (AppId, SecretKey, Username, Password) are not set.");
                return;
            }

            _logger.LogInformation("WorkNest Grandstream Polling Service started (refresh every {Seconds}s).", _gwn.RefreshSeconds);
            try { await Task.Delay(_startupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

            try
            {
                await Task.WhenAll(
                    RunEveryAsync(TimeSpan.FromSeconds(_gwn.RefreshSeconds), () => _gwn.RefreshCoreAsync(stoppingToken), stoppingToken),
                    RunEveryAsync(StatsInterval, () => _gwn.RefreshStatsAsync(stoppingToken), stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            _logger.LogInformation("WorkNest Grandstream Polling Service stopping.");
        }

        private async Task RunEveryAsync(TimeSpan interval, Func<Task> action, CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(interval);
            await RunSafeAsync(action, stoppingToken);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                    await RunSafeAsync(action, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        private async Task RunSafeAsync(Func<Task> action, CancellationToken stoppingToken)
        {
            try { if (await _gate.IsOnAsync(stoppingToken)) await action(); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { _logger.LogError(ex, "Error occurred during Grandstream polling."); }
        }
    }
}
