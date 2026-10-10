using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Reports;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Background service that e-mails the weekly report (WeeklyReportGenerator) to the active super admins plus
    /// Reports:Weekly:ExtraRecipients. Checks every 15 minutes; sends once per ISO week, from the configured day and
    /// hour (Pakistan time, default Monday 09:00) until the end of that week, so a server that was down at 09:00
    /// still sends it later the same week.
    /// "Once" holds across restarts and several IIS instances: each run is claimed in WN_ScheduledReportRuns
    /// (UNIQUE ReportKey + PeriodKey) before anything is sent; whoever inserts the row sends, the others skip.
    /// A run where no e-mail went out (Failed) is retried after an hour. If the table has not been created yet,
    /// a marker file in App_Data stands in for it (at most once per week per server).
    /// Config section "Reports:Weekly": Enabled (default true), DayOfWeek (Monday), Hour (9), ExtraRecipients (comma list).
    /// </summary>
    public class WeeklyReportService : BackgroundService
    {
        public const string ReportKey = "weekly";

        private readonly ILogger<WeeklyReportService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly IBusinessClock _clock;
        private readonly IHostEnvironment _env;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(15);
        private readonly TimeSpan _startupDelay = TimeSpan.FromMinutes(2);
        private readonly TimeSpan _retryAfter = TimeSpan.FromHours(1);

        // Weeks this instance knows are done (sent here or by another instance), so it stops asking the database.
        private readonly HashSet<string> _doneWeeks = new();
        // UTC time before which a failed week is not tried again by this instance.
        private DateTime _holdBackUntilUtc = DateTime.MinValue;

        public WeeklyReportService(ILogger<WeeklyReportService> logger, IServiceScopeFactory scopeFactory, IConfiguration configuration,
            IBusinessClock clock, IHostEnvironment env)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _clock = clock;
            _env = env;
        }

        /// <summary>Reads "Reports:Weekly" (also used by ReportsController for the extra recipients).</summary>
        public static WeeklyReportOptions ReadOptions(IConfiguration configuration)
        {
            var s = configuration.GetSection("Reports:Weekly");
            var o = new WeeklyReportOptions
            {
                Enabled = !string.Equals(s["Enabled"], "false", StringComparison.OrdinalIgnoreCase)
            };
            if (Enum.TryParse<DayOfWeek>(s["DayOfWeek"], true, out var day) && Enum.IsDefined(day)) o.DayOfWeek = day;
            if (int.TryParse(s["Hour"], out var hour) && hour is >= 0 and <= 23) o.Hour = hour;
            o.ExtraRecipients = (s["ExtraRecipients"] ?? "")
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            return o;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!ReadOptions(_configuration).Enabled)
            {
                _logger.LogInformation("WorkNest Weekly Report Service is disabled (Reports:Weekly:Enabled = false).");
                return;
            }

            _logger.LogInformation("WorkNest Weekly Report Service started.");
            try { await Task.Delay(_startupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var options = ReadOptions(_configuration);
                    if (options.Enabled)
                        await RunOnceAsync(options, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during the weekly report.");
                }

                try { await Task.Delay(_checkInterval, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }

        private async Task RunOnceAsync(WeeklyReportOptions options, CancellationToken stoppingToken)
        {
            var now = _clock.Now;
            // The scheduled moment in this ISO week (weeks run Monday..Sunday).
            var monday = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
            var due = monday.AddDays(((int)options.DayOfWeek + 6) % 7).AddHours(options.Hour);
            if (now < due) return;

            var periodKey = WeeklyReportGenerator.PeriodKeyFor(now);
            if (_doneWeeks.Contains(periodKey) || DateTime.UtcNow < _holdBackUntilUtc) return;

            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IReportRepository>();
            var claim = await repo.TryClaimRunAsync(ReportKey, periodKey, now, now - _retryAfter);
            switch (claim)
            {
                case ReportClaimResult.Taken:
                    _doneWeeks.Add(periodKey);
                    return;
                case ReportClaimResult.RetryLater:
                    return;
                case ReportClaimResult.TableMissing:
                    await RunWithMarkerFileAsync(scope.ServiceProvider, options, periodKey, stoppingToken);
                    return;
            }

            // Claimed: this instance sends. Whatever happens, the row is completed so the week is not left "Sending".
            WeeklyReportSendResultDto result;
            var recipients = new List<string>();
            try
            {
                var generator = scope.ServiceProvider.GetRequiredService<IWeeklyReportGenerator>();
                recipients = await generator.GetRecipientsAsync(options.ExtraRecipients);
                var report = await generator.BuildAsync();
                result = await generator.SendAsync(report, recipients, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                result = new WeeklyReportSendResultDto { Status = "Failed", Error = ex.Message };
                _logger.LogError(ex, "Weekly report {PeriodKey} could not be built or sent.", periodKey);
            }

            var sentTo = result.Recipients.Count > 0 ? result.Recipients : recipients;
            await repo.CompleteRunAsync(ReportKey, periodKey, result.Status, string.Join(", ", sentTo), result.Error, _clock.Now);
            Log(periodKey, result);
            if (result.Status == "Failed") _holdBackUntilUtc = DateTime.UtcNow.Add(_retryAfter);
            else _doneWeeks.Add(periodKey);
        }

        /// <summary>
        /// Fallback while WN_ScheduledReportRuns does not exist: App_Data/weekly-report-{week}.sent is created
        /// (CreateNew, so only one run on this server gets it) before sending, and removed again if nothing was sent.
        /// </summary>
        private async Task RunWithMarkerFileAsync(IServiceProvider services, WeeklyReportOptions options, string periodKey, CancellationToken stoppingToken)
        {
            var dir = Path.Combine(_env.ContentRootPath, "App_Data");
            Directory.CreateDirectory(dir);
            var marker = Path.Combine(dir, $"weekly-report-{periodKey}.sent");
            try
            {
                await using var claim = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var text = System.Text.Encoding.UTF8.GetBytes($"Claimed {_clock.Now:yyyy-MM-dd HH:mm:ss} (Pakistan time) by {Environment.MachineName}#{Environment.ProcessId}\n");
                await claim.WriteAsync(text, stoppingToken);
            }
            catch (IOException)
            {
                _doneWeeks.Add(periodKey);   // already sent (or being sent) from this server
                return;
            }

            _logger.LogWarning("WN_ScheduledReportRuns is missing (run Database/Reports/WN_ScheduledReportRuns.txt); using {Marker} to send the weekly report once.", marker);
            WeeklyReportSendResultDto result;
            try
            {
                var generator = services.GetRequiredService<IWeeklyReportGenerator>();
                var recipients = await generator.GetRecipientsAsync(options.ExtraRecipients);
                var report = await generator.BuildAsync();
                result = await generator.SendAsync(report, recipients, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                result = new WeeklyReportSendResultDto { Status = "Failed", Error = ex.Message };
                _logger.LogError(ex, "Weekly report {PeriodKey} could not be built or sent.", periodKey);
            }

            Log(periodKey, result);
            if (result.Status == "Failed")
            {
                try { File.Delete(marker); } catch (IOException) { }
                _holdBackUntilUtc = DateTime.UtcNow.Add(_retryAfter);
            }
            else
            {
                try { await File.AppendAllTextAsync(marker, $"{result.Status} to {string.Join(", ", result.Recipients)}\n", stoppingToken); } catch (IOException) { }
                _doneWeeks.Add(periodKey);
            }
        }

        private void Log(string periodKey, WeeklyReportSendResultDto result)
        {
            if (result.Status == "Failed")
                _logger.LogWarning("Weekly report {PeriodKey} was not sent: {Error}. Retrying in {Hours} hour(s).", periodKey, result.Error, _retryAfter.TotalHours);
            else
                _logger.LogInformation("Weekly report {PeriodKey}: {Status}, sent to {Recipients}{Failed}.", periodKey, result.Status,
                    string.Join(", ", result.Recipients), result.Failed.Count > 0 ? $"; failed for {string.Join(", ", result.Failed)}" : "");
        }
    }
}
