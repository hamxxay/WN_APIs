using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Background job periodically expiring branch transfer requests older than 72 hours.
    /// </summary>
    public class TransferExpiryBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TransferExpiryBackgroundService> _logger;

        public TransferExpiryBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<TransferExpiryBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Transfer Expiry Background Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
                        var expiredCount = await repo.ExpireOldTransferRequestsAsync();
                        if (expiredCount > 0)
                        {
                            _logger.LogInformation("Expired {ExpiredCount} pending transfer requests that exceeded 72 hours.", expiredCount);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during transfer request auto-expiry check.");
                }

                // Check every 30 minutes
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("Transfer Expiry Background Service is stopping.");
        }
    }
}
