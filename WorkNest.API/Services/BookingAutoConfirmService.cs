using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Background service that runs every 5 minutes: a Pending booking whose first invoice is Paid
    /// (OrderStatus "Paid", or legacy 2) becomes Confirmed. Invoices are marked paid outside the API
    /// (accounting / manually), so this check is what moves the booking on.
    /// Disable with "BookingAutoConfirm:Enabled": false in appsettings.
    /// </summary>
    public class BookingAutoConfirmService : BackgroundService
    {
        private readonly ILogger<BookingAutoConfirmService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly bool _enabled;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(30);

        public BookingAutoConfirmService(ILogger<BookingAutoConfirmService> logger, IServiceScopeFactory scopeFactory, IConfiguration configuration)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _enabled = !string.Equals(configuration["BookingAutoConfirm:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("WorkNest Booking Auto-Confirm Service is disabled (BookingAutoConfirm:Enabled = false).");
                return;
            }

            _logger.LogInformation("WorkNest Booking Auto-Confirm Service started.");
            try { await Task.Delay(_startupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var bookings = scope.ServiceProvider.GetRequiredService<IBookingService>();
                    var confirmed = await bookings.ConfirmPaidBookingsAsync();
                    if (confirmed.Count > 0)
                        _logger.LogInformation("Booking auto-confirm: {Count} paid booking(s) confirmed: {Ids}", confirmed.Count, string.Join(", ", confirmed));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during booking auto-confirm.");
                }

                try { await Task.Delay(_checkInterval, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
