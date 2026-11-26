using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Takes the door access of Day Pass people away once the pass has ended (or was cancelled): every 5 minutes, and logs AccessRemoved /
    /// Used in the pass history. Runs where the Hikvision sync runs (HikSync:Enabled, the one server that reaches the machines) and
    /// follows the Hik Sync on/off switch. The machines also stop the person by themselves at the pass's valid-until time.
    /// </summary>
    public class DayPassAccessService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly HikSyncOptions _options;
        private readonly ILogger<DayPassAccessService> _logger;
        private readonly ServiceSwitchGate _gate;

        public DayPassAccessService(IServiceProvider services, HikSyncOptions options, ILogger<DayPassAccessService> logger, IServiceSwitch serviceSwitch)
        {
            _services = services;
            _options = options;
            _logger = logger;
            _gate = new ServiceSwitchGate(serviceSwitch, BackgroundServiceCatalog.HikSync, logger);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Day pass access removal is off (HikSync:Enabled = false).");
                return;
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
            do
            {
                try
                {
                    if (await _gate.IsOnAsync(stoppingToken)) await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { _logger.LogError(ex, "Day pass access removal crashed"); }
            }
            while (await WaitAsync(timer, stoppingToken));
        }

        private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
        {
            try { return await timer.WaitForNextTickAsync(ct); } catch (OperationCanceledException) { return false; }
        }

        public async Task RunOnceAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IDayPassService>();
            if (!await service.IsInstalledAsync()) return;
            var due = await service.AccessDueAsync();
            foreach (var d in due)
            {
                if (ct.IsCancellationRequested) break;
                try { await service.RemoveAccessAsync(d); }
                catch (Exception ex) { _logger.LogWarning(ex, "Door access of day pass {Pass} could not be removed; retrying next time", d.PassNumber); }
            }
            if (due.Count > 0) _logger.LogInformation("Day pass access: {Count} pass(es) handled.", due.Count);
        }
    }
}
