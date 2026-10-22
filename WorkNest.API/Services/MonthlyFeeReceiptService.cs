using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Background service that runs every hour (FeeReceipts:MonthlyCheckMinutes): a multi-month invoice is paid at once,
    /// receipt 1 (saved by staff) covers month 1, and months 2..N get their own receipt and voucher when the month starts.
    /// dbo.WN_FeeReceipts_CreateDueMonthly (script RC_02) creates every receipt due by today (Pakistan date) that is not
    /// created yet, so a server that was down catches up on the next cycle. On unless "FeeReceipts:MonthlyEnabled": false.
    /// </summary>
    public class MonthlyFeeReceiptService : BackgroundService
    {
        private readonly ILogger<MonthlyFeeReceiptService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IBusinessClock _clock;
        private readonly bool _enabled;
        private readonly TimeSpan _checkInterval;
        private readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(60);

        public MonthlyFeeReceiptService(ILogger<MonthlyFeeReceiptService> logger, IServiceScopeFactory scopeFactory, IConfiguration configuration, IBusinessClock clock)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _clock = clock;
            _enabled = !string.Equals(configuration["FeeReceipts:MonthlyEnabled"], "false", StringComparison.OrdinalIgnoreCase);
            _checkInterval = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("FeeReceipts:MonthlyCheckMinutes", 60)));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("WorkNest Monthly Fee Receipt Service is disabled (FeeReceipts:MonthlyEnabled = false).");
                return;
            }

            _logger.LogInformation("WorkNest Monthly Fee Receipt Service started.");
            try { await Task.Delay(_startupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while creating the monthly fee receipts.");
                }

                try { await Task.Delay(_checkInterval, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>One cycle: creates the monthly receipts due today; returns how many were created (0 before RC_02).</summary>
        public async Task<int> RunOnceAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDbRepository>();
                var created = (await db.CreateDueMonthlyFeeReceiptsDbAsync(_clock.Today)).ToList();
                foreach (var row in created)
                {
                    object? V(string key) => row.TryGetValue(key, out var v) ? v : null;
                    _logger.LogInformation("Monthly fee receipt {Code} created for invoice {InvoiceNumber}: month {MonthNo} of {MonthCount} ({BillingMonth:MMM yyyy}), amount {Amount}.",
                        V("Code"), V("InvoiceNumber"), V("MonthNo"),
                        V("MonthCount"), V("BillingMonth"), V("Amount"));
                }
                if (created.Count > 0)
                    _logger.LogInformation("Monthly fee receipts: {Count} receipt(s) created.", created.Count);
                return created.Count;
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 2812)
            {
                _logger.LogWarning("Monthly fee receipts: dbo.WN_FeeReceipts_CreateDueMonthly is missing; run Database/FeeReceipts/RC_02_monthly_receipts.txt.");
                return 0;
            }
        }
    }
}
