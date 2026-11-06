using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Approvals expiry: every hour runs <see cref="ApprovalService.ExpireDueAsync"/> (WN_Approval_Requests_ExpireDue), which
    /// expires the requests whose link ran out and e-mails the people who made them. Never stops or crashes the host: a
    /// failure is logged and the next hour tries again. Before script APPROVAL_01 it logs once and stays quiet.
    /// </summary>
    public sealed class ApprovalExpiryService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<ApprovalExpiryService> _logger;
        private readonly TimeSpan _startupDelay;
        private readonly TimeSpan _interval;
        private bool _warnedMissing;

        public ApprovalExpiryService(IServiceScopeFactory scopes, ILogger<ApprovalExpiryService> logger)
            : this(scopes, logger, TimeSpan.FromSeconds(90), TimeSpan.FromHours(1)) { }

        /// <summary>For tests: chosen delay and interval.</summary>
        public ApprovalExpiryService(IServiceScopeFactory scopes, ILogger<ApprovalExpiryService> logger, TimeSpan startupDelay, TimeSpan interval)
        {
            _scopes = scopes; _logger = logger; _startupDelay = startupDelay; _interval = interval;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(_startupDelay, stoppingToken);
                using var timer = new PeriodicTimer(_interval);
                do { await TickAsync(stoppingToken); }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }

        /// <summary>One run. Returns how many requests expired, or null when it could not run. Never throws (except on shutdown).</summary>
        public async Task<int?> TickAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var expired = await scope.ServiceProvider.GetRequiredService<ApprovalService>().ExpireDueAsync(ct);
                _warnedMissing = false;
                if (expired > 0) _logger.LogInformation("Approvals: {Count} request(s) expired.", expired);
                return expired;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (ApprovalsMissingException)
            {
                if (!_warnedMissing) _logger.LogInformation("Approvals expiry idle: the database script APPROVAL_01_approvals has not run.");
                _warnedMissing = true;
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during the approvals expiry run.");
                return null;
            }
        }
    }
}
