using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Every hour (09:00 to 20:00 Pakistan time): reminds the sales manager, in the app and by e-mail, of each quotation that has waited
    /// 24 hours for their approval (then once a day while it waits). It never approves or sends anything. Switch QuotationApprovalReminders
    /// (CMS Services); quiet before the script QAPR_01 has run. Config: QuotationApprovalReminders:Enabled (default on).
    /// </summary>
    public class QuotationApprovalReminderService : BackgroundService
    {
        private readonly ILogger<QuotationApprovalReminderService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IBusinessClock _clock;
        private readonly bool _enabled;
        private readonly ServiceSwitchGate _gate;
        private readonly TimeSpan _interval = TimeSpan.FromHours(1);
        private readonly TimeSpan _startupDelay = TimeSpan.FromMinutes(3);
        private const int SendFromHour = 9;
        private const int SendUntilHour = 20;

        public QuotationApprovalReminderService(ILogger<QuotationApprovalReminderService> logger, IServiceScopeFactory scopeFactory,
            IConfiguration configuration, IBusinessClock clock, IServiceSwitch serviceSwitch)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _clock = clock;
            _enabled = BackgroundServiceCatalog.Get(BackgroundServiceCatalog.QuotationApprovalReminders).IsConfigEnabled(configuration);
            _gate = new ServiceSwitchGate(serviceSwitch, BackgroundServiceCatalog.QuotationApprovalReminders, logger);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("Quotation Approval Reminder Service is disabled (QuotationApprovalReminders:Enabled = false).");
                return;
            }
            try { await Task.Delay(_startupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    int hour = _clock.Now.Hour;
                    if (hour >= SendFromHour && hour < SendUntilHour && await _gate.IsOnAsync(stoppingToken))
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var approvals = scope.ServiceProvider.GetRequiredService<IQuotationApprovalService>();
                        int n = await approvals.SendDueRemindersAsync(stoppingToken);
                        if (n > 0) _logger.LogInformation("Quotation approval reminders sent: {Count}.", n);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { _logger.LogError(ex, "Error occurred during quotation approval reminders."); }

                try { await Task.Delay(_interval, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
