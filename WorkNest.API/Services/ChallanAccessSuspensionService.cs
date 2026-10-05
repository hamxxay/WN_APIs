using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Background service that runs every hour: when a booking's challan is unpaid and its due date has
    /// passed, door access for that booking is suspended on the Hikvision machines (room + Entrance)
    /// until the challan is Paid. Manual extensions are honoured until their date.
    /// Disable with "HikAccessSuspension:Enabled": false in appsettings.
    /// </summary>
    public class ChallanAccessSuspensionService : BackgroundService
    {
        private readonly ILogger<ChallanAccessSuspensionService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly bool _enabled;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);
        private readonly TimeSpan _startupDelay = TimeSpan.FromMinutes(2);

        public ChallanAccessSuspensionService(ILogger<ChallanAccessSuspensionService> logger, IServiceScopeFactory scopeFactory, IConfiguration configuration)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _enabled = !string.Equals(configuration["HikAccessSuspension:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("WorkNest Challan Access Suspension Service is disabled (HikAccessSuspension:Enabled = false).");
                return;
            }

            _logger.LogInformation("WorkNest Challan Access Suspension Service started.");
            try { await Task.Delay(_startupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var suspension = scope.ServiceProvider.GetRequiredService<IHikAccessSuspensionService>();
                    var applied = await suspension.RunAccessSuspensionCycleAsync(stoppingToken);
                    _logger.LogInformation("Challan access suspension cycle complete. {Count} booking(s) updated on the machines.", applied);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during challan access suspension cycle.");
                }

                try { await Task.Delay(_checkInterval, stoppingToken); } catch (OperationCanceledException) { break; }
            }

            _logger.LogInformation("WorkNest Challan Access Suspension Service stopping.");
        }
    }
}
