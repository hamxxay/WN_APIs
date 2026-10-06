using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Agreement;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Background service that runs every hour and emails customers whose agreement still waits for their signature
    /// (reminder #1 after 3 days, #2 after 7 days, then stops). Only sends between 09:00 and 20:00 Pakistan time.
    /// A failed email is not recorded, so it is retried on a later run, but the agreement is held back for 6 hours
    /// (in memory) to avoid hammering a bad address or a down SMTP server.
    /// Config section "AgreementReminders": Enabled, FirstAfterDays, SecondAfterDays, MaxReminders, PortalUrl, AttachPdf.
    /// </summary>
    public class AgreementReminderService : BackgroundService
    {
        private readonly ILogger<AgreementReminderService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly IBusinessClock _clock;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);
        private readonly TimeSpan _startupDelay = TimeSpan.FromMinutes(2);
        private readonly TimeSpan _failureHoldBack = TimeSpan.FromHours(6);
        private const int SendFromHour = 9;   // 09:00 Pakistan time
        private const int SendUntilHour = 20; // up to 20:00 Pakistan time

        // AgreementId -> UTC time until which it is not retried after a failure.
        private readonly Dictionary<int, DateTime> _heldBackUntilUtc = new();

        public AgreementReminderService(ILogger<AgreementReminderService> logger, IServiceScopeFactory scopeFactory, IConfiguration configuration, IBusinessClock clock)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _clock = clock;
        }

        private AgreementReminderOptions ReadOptions()
        {
            var s = _configuration.GetSection("AgreementReminders");
            var o = new AgreementReminderOptions();
            o.Enabled = !string.Equals(s["Enabled"], "false", StringComparison.OrdinalIgnoreCase);
            if (int.TryParse(s["FirstAfterDays"], out var first) && first > 0) o.FirstAfterDays = first;
            if (int.TryParse(s["SecondAfterDays"], out var second) && second > 0) o.SecondAfterDays = second;
            if (int.TryParse(s["MaxReminders"], out var max) && max >= 0) o.MaxReminders = max;
            if (!string.IsNullOrWhiteSpace(s["PortalUrl"])) o.PortalUrl = s["PortalUrl"]!.Trim();
            o.AttachPdf = !string.Equals(s["AttachPdf"], "false", StringComparison.OrdinalIgnoreCase);
            return o;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!ReadOptions().Enabled)
            {
                _logger.LogInformation("WorkNest Agreement Reminder Service is disabled (AgreementReminders:Enabled = false).");
                return;
            }

            _logger.LogInformation("WorkNest Agreement Reminder Service started.");
            try { await Task.Delay(_startupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var options = ReadOptions();
                    int hour = _clock.Now.Hour;
                    if (options.Enabled && hour >= SendFromHour && hour < SendUntilHour)
                        await RunOnceAsync(options, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during agreement reminders.");
                }

                try { await Task.Delay(_checkInterval, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }

        private async Task RunOnceAsync(AgreementReminderOptions options, CancellationToken stoppingToken)
        {
            var nowUtc = DateTime.UtcNow;
            foreach (var expired in _heldBackUntilUtc.Where(kv => kv.Value <= nowUtc).Select(kv => kv.Key).ToList())
                _heldBackUntilUtc.Remove(expired);

            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetService<IAgreementReminderProcessor>()
                            ?? ActivatorUtilities.CreateInstance<AgreementReminderProcessor>(scope.ServiceProvider);

            var outcomes = await processor.SendDueRemindersAsync(options, id => _heldBackUntilUtc.ContainsKey(id), stoppingToken);

            foreach (var o in outcomes)
            {
                switch (o.Result)
                {
                    case "Sent":
                        _logger.LogInformation("Agreement reminder #{ReminderNumber} sent for agreement #{AgreementId} (quotation {QuotationNumber}) to {Email}.",
                            o.ReminderNumber, o.AgreementId, o.QuotationNumber, o.Email);
                        break;
                    case "SentNotRecorded":
                        _heldBackUntilUtc[o.AgreementId] = nowUtc.Add(_failureHoldBack);
                        _logger.LogError("Agreement reminder #{ReminderNumber} for agreement #{AgreementId} (quotation {QuotationNumber}) was emailed to {Email} but could not be recorded: {Error}. Held back for {Hours} hours.",
                            o.ReminderNumber, o.AgreementId, o.QuotationNumber, o.Email, o.Error, _failureHoldBack.TotalHours);
                        break;
                    case "Failed":
                        _heldBackUntilUtc[o.AgreementId] = nowUtc.Add(_failureHoldBack);
                        _logger.LogWarning("Agreement reminder #{ReminderNumber} for agreement #{AgreementId} (quotation {QuotationNumber}) to {Email} failed: {Error}. Retrying in {Hours} hours.",
                            o.ReminderNumber, o.AgreementId, o.QuotationNumber, o.Email, o.Error, _failureHoldBack.TotalHours);
                        break;
                    default: // Locked / NoLongerDue: another instance handled it or the customer signed meanwhile
                        _logger.LogDebug("Agreement reminder for agreement #{AgreementId} skipped: {Result}.", o.AgreementId, o.Result);
                        break;
                }
            }
        }
    }
}
