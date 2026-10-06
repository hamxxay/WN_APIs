using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Background service that keeps the UniFi dashboard state fresh (same timers as "Unifi UI" server.js):
    /// sites / devices / client counts every Unifi:RefreshSeconds (default 60, records a history sample),
    /// ISP metrics every 5 minutes, and the console event log warmed every 50 seconds.
    /// Does nothing when Unifi:ApiKey is not set. Disable with "Unifi:Enabled": false in appsettings.
    /// </summary>
    public class UnifiPollingService : BackgroundService
    {
        private readonly ILogger<UnifiPollingService> _logger;
        private readonly IUnifiService _unifi;
        private readonly bool _enabled;
        private readonly TimeSpan _ispInterval = TimeSpan.FromSeconds(300); // EA endpoints are rate limited to 100 req/min
        private readonly TimeSpan _logsInterval = TimeSpan.FromSeconds(50);
        private readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(5);

        public UnifiPollingService(ILogger<UnifiPollingService> logger, IUnifiService unifi, IConfiguration configuration)
        {
            _logger = logger;
            _unifi = unifi;
            _enabled = !string.Equals(configuration["Unifi:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("WorkNest UniFi Polling Service is disabled (Unifi:Enabled = false).");
                return;
            }
            if (!_unifi.IsConfigured)
            {
                _logger.LogWarning("WorkNest UniFi Polling Service not started: Unifi:ApiKey is not set in appsettings.");
                return;
            }

            _logger.LogInformation("WorkNest UniFi Polling Service started (refresh every {Seconds}s).", _unifi.RefreshSeconds);
            try { await Task.Delay(_startupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

            try
            {
                await _unifi.InitializeAsync(stoppingToken);
                await Task.WhenAll(_unifi.RefreshCoreAsync(stoppingToken), _unifi.RefreshIspAsync(stoppingToken));

                await Task.WhenAll(
                    RunEveryAsync(TimeSpan.FromSeconds(_unifi.RefreshSeconds), () => _unifi.RefreshCoreAsync(stoppingToken), false, stoppingToken),
                    RunEveryAsync(_ispInterval, () => _unifi.RefreshIspAsync(stoppingToken), false, stoppingToken),
                    // Keep the event log warm so client/device history opens instantly.
                    RunEveryAsync(_logsInterval, _unifi.WarmLogsAsync, true, stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }

            _logger.LogInformation("WorkNest UniFi Polling Service stopping.");
        }

        private async Task RunEveryAsync(TimeSpan interval, Func<Task> action, bool runNow, CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(interval);
            if (runNow) await RunSafeAsync(action);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                    await RunSafeAsync(action);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        private async Task RunSafeAsync(Func<Task> action)
        {
            try { await action(); }
            catch (Exception ex) { _logger.LogError(ex, "Error occurred during UniFi polling."); }
        }
    }
}
