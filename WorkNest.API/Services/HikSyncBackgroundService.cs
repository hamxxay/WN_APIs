using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Runs the Hikvision sync on the same schedule as the HIK Node scheduler it replaces:
    /// online check every 2 min, maintenance bundle every 5 min, enrollment watcher every 90 s,
    /// clock sync daily at 04:00 (Pakistan) and once at startup.
    /// Off unless "HikSync:Enabled" is true — enable it on ONE server only (the one that can reach the machines),
    /// never on a developer machine pointed at the live database.
    /// </summary>
    public class HikSyncBackgroundService : BackgroundService
    {
        private readonly IHikSyncService _sync;
        private readonly IBusinessClock _clock;
        private readonly HikSyncOptions _options;
        private readonly ILogger<HikSyncBackgroundService> _logger;

        public HikSyncBackgroundService(IHikSyncService sync, IBusinessClock clock, HikSyncOptions options, ILogger<HikSyncBackgroundService> logger)
        {
            _sync = sync;
            _clock = clock;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("HIK sync is disabled (HikSync:Enabled = false). Jobs can still be run from api/hik/sync.");
                return;
            }
            _logger.LogInformation("HIK sync started: watcher every 90s, maintenance every 5 min, online check every 2 min, clock sync daily 04:00.");

            try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); } catch (OperationCanceledException) { return; }
            await _sync.RunJobAsync(HikSyncService.JobOnline);
            await _sync.RunJobAsync(HikSyncService.JobClock);
            await _sync.RunJobAsync(HikSyncService.JobCredentials);
            await _sync.RunJobAsync(HikSyncService.JobWatch);

            await Task.WhenAll(
                Every(TimeSpan.FromMinutes(2), HikSyncService.JobOnline, stoppingToken),
                Every(TimeSpan.FromMinutes(5), HikSyncService.JobMaintenance, stoppingToken),
                Every(TimeSpan.FromSeconds(90), HikSyncService.JobWatch, stoppingToken),
                DailyClockSync(stoppingToken));
        }

        private async Task Every(TimeSpan interval, string job, CancellationToken ct)
        {
            using var timer = new PeriodicTimer(interval);
            try
            {
                while (await timer.WaitForNextTickAsync(ct))
                {
                    try { await _sync.RunJobAsync(job); }
                    catch (Exception ex) { _logger.LogError(ex, "HIK sync job {Job} crashed", job); }
                }
            }
            catch (OperationCanceledException) { /* shutting down */ }
        }

        private async Task DailyClockSync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var now = _clock.Now;
                var next = now.Date.AddHours(4);
                if (next <= now) next = next.AddDays(1);
                try { await Task.Delay(next - now, ct); } catch (OperationCanceledException) { return; }
                try { await _sync.RunJobAsync(HikSyncService.JobClock); }
                catch (Exception ex) { _logger.LogError(ex, "HIK clock sync crashed"); }
            }
        }
    }
}
