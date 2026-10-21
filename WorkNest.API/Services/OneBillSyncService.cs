using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Background service that runs every 2 minutes (OneBill:SyncSeconds): invoices of a One Bill location that are not
    /// on EzPay yet (new recurring / custom / surcharge invoices, and earlier failures) are sent to EzPay.
    /// Invoices created from the invoice screens are sent right away; this catches the rest. Each invoice is tried at
    /// most OneBill:MaxAttempts (5) times. Off unless "OneBill:Enabled": true and OneBill:BaseUrl / Username / Password are set.
    /// </summary>
    public class OneBillSyncService : BackgroundService
    {
        private readonly ILogger<OneBillSyncService> _logger;
        private readonly IOneBillService _oneBill;
        private readonly TimeSpan _checkInterval;
        private readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(45);

        public OneBillSyncService(ILogger<OneBillSyncService> logger, IOneBillService oneBill, IConfiguration configuration)
        {
            _logger = logger;
            _oneBill = oneBill;
            _checkInterval = TimeSpan.FromSeconds(Math.Max(30, configuration.GetValue("OneBill:SyncSeconds", 120)));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_oneBill.IsConfigured)
            {
                _logger.LogInformation("WorkNest One Bill Sync Service is disabled (OneBill:Enabled is not true, or OneBill:BaseUrl / Username / Password is empty).");
                return;
            }

            _logger.LogInformation("WorkNest One Bill Sync Service started.");
            try { await Task.Delay(_startupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var pending = await _oneBill.GetPendingAsync(50, stoppingToken);
                    var created = 0;
                    foreach (var invoiceId in pending)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        if (await _oneBill.EnsureAsync(invoiceId, stoppingToken) is not null) created++;
                    }
                    if (pending.Count > 0)
                        _logger.LogInformation("One Bill sync: {Created} of {Count} pending invoice(s) created on EzPay.", created, pending.Count);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 2812)
                {
                    _logger.LogWarning("One Bill sync: dbo.WN_OneBill_GetPending is missing; run Database/OneBill/OB_01_one_bill.txt.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during One Bill sync.");
                }

                try { await Task.Delay(_checkInterval, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
