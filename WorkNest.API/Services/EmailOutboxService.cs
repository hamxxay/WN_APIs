using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Pending emails (dbo.WN_EmailOutbox, script SS_02_email_sending): every minute while the Email Sending switch is on,
    /// sends the e-mails held while it was off (up to 20 a minute, oldest first). A failure puts the e-mail back to Pending;
    /// after 3 attempts it is Failed (a super admin can Send now or discard it). Discarded e-mails are never sent.
    /// Does nothing before SS_02.
    /// </summary>
    public class EmailOutboxService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
        private const int BatchSize = 20;

        private readonly ILogger<EmailOutboxService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IEmailOutbox _outbox;
        private readonly ServiceSwitchGate _emailGate; // Email Sending switch: off = held e-mails wait

        public EmailOutboxService(ILogger<EmailOutboxService> logger, IServiceProvider serviceProvider, IEmailOutbox outbox,
            IServiceSwitch serviceSwitch)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _outbox = outbox;
            _emailGate = new ServiceSwitchGate(serviceSwitch, BackgroundServiceCatalog.EmailSending, logger);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var missingLogged = false;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (await _emailGate.IsOnAsync(stoppingToken))
                        await SendPendingAsync(stoppingToken);
                    missingLogged = false;
                }
                catch (EmailOutboxMissingException)
                {
                    if (!missingLogged) _logger.LogInformation("Pending emails: {Text}", EmailOutboxMissingException.Text);
                    missingLogged = true;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Pending emails: the send cycle failed.");
                }

                try { await Task.Delay(Interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task SendPendingAsync(CancellationToken ct)
        {
            var claimed = await _outbox.ClaimAsync(null, BatchSize, ct);
            if (claimed.Count == 0) return;
            using var scope = _serviceProvider.CreateScope();
            var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
            foreach (var item in claimed)
            {
                try
                {
                    await email.SendHeldAsync(item.MessageJson);
                    await _outbox.FinishAsync(item.Id, true, null, CancellationToken.None);
                    _logger.LogInformation("Pending e-mail #{Id} ({Kind}) sent to {To}.", item.Id, item.Kind, item.ToEmail);
                }
                catch (Exception ex)
                {
                    await _outbox.FinishAsync(item.Id, false, ex.Message, CancellationToken.None);
                    _logger.LogWarning(ex, "Pending e-mail #{Id} ({Kind}) to {To} could not be sent (attempt {Attempt}).", item.Id, item.Kind, item.ToEmail, item.Attempts + 1);
                }
            }
        }
    }
}
