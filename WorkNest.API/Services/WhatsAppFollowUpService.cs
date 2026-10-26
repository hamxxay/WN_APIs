using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Simple Inquiries (INQ_01): every WhatsApp:FollowUpTickSeconds (default 30) runs <see cref="WhatsAppFollowUpProcessor"/>:
    /// a customer who typed free text at the bot's main menu and then went quiet for WhatsApp:FollowUpQuietSeconds (default
    /// 120) gets "Our sales representative will contact you shortly." and one general inquiry is created. On / off with the
    /// WhatsAppFollowUp service switch (Services page) and "WhatsApp:FollowUpEnabled": false. Nothing is sent while WhatsApp
    /// is not configured; before INQ_01 every tick finds nothing.
    /// </summary>
    public sealed class WhatsAppFollowUpService : BackgroundService
    {
        public const int DefaultTickSeconds = 30;

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<WhatsAppFollowUpService> _logger;
        private readonly ServiceSwitchGate _gate;
        private readonly bool _enabled;
        private readonly TimeSpan _tick;
        private readonly TimeSpan _startupDelay;
        private bool _warnedNotConfigured;

        public WhatsAppFollowUpService(IServiceScopeFactory scopes, IConfiguration configuration, IServiceSwitch serviceSwitch,
            ILogger<WhatsAppFollowUpService> logger)
            : this(scopes, configuration, serviceSwitch, logger, TimeSpan.FromSeconds(20)) { }

        /// <summary>For tests: a chosen start-up delay.</summary>
        public WhatsAppFollowUpService(IServiceScopeFactory scopes, IConfiguration configuration, IServiceSwitch serviceSwitch,
            ILogger<WhatsAppFollowUpService> logger, TimeSpan startupDelay)
        {
            _scopes = scopes;
            _logger = logger;
            _gate = new ServiceSwitchGate(serviceSwitch, BackgroundServiceCatalog.WhatsAppFollowUp, logger);
            _enabled = BackgroundServiceCatalog.Get(BackgroundServiceCatalog.WhatsAppFollowUp).IsConfigEnabled(configuration);
            _tick = TimeSpan.FromSeconds(int.TryParse(configuration["WhatsApp:FollowUpTickSeconds"], out var s) && s > 0 ? s : DefaultTickSeconds);
            _startupDelay = startupDelay;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("WorkNest WhatsApp Follow-Up Service is disabled (WhatsApp:FollowUpEnabled = false).");
                return;
            }
            _logger.LogInformation("WorkNest WhatsApp Follow-Up Service started (every {Seconds}s).", _tick.TotalSeconds);
            try { await Task.Delay(_startupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

            using var timer = new PeriodicTimer(_tick);
            try
            {
                do { await TickAsync(stoppingToken); }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            _logger.LogInformation("WorkNest WhatsApp Follow-Up Service stopping.");
        }

        /// <summary>One tick: the switch, then the processor in its own scope. Never throws (except on shutdown).</summary>
        public async Task<WhatsAppFollowUpProcessor.TickResult?> TickAsync(CancellationToken ct)
        {
            try
            {
                if (!await _gate.IsOnAsync(ct)) return null;
                using var scope = _scopes.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<IWhatsAppClient>();
                if (!client.IsConfigured)
                {
                    if (!_warnedNotConfigured) _logger.LogWarning("WhatsApp follow-ups paused: WhatsApp is not configured (WhatsApp:Token / PhoneNumberId).");
                    _warnedNotConfigured = true;
                    return null;
                }
                _warnedNotConfigured = false;
                return await scope.ServiceProvider.GetRequiredService<WhatsAppFollowUpProcessor>().RunOnceAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during the WhatsApp follow-up.");
                return null;
            }
        }
    }
}
