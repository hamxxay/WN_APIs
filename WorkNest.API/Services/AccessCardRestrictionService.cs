using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Background service that runs daily to re-restrict access cards for bookings whose partial payment period has lapsed
    /// and remaining balance is unpaid.
    /// </summary>
    public class AccessCardRestrictionService : BackgroundService
    {
        private readonly ILogger<AccessCardRestrictionService> _logger;
        private readonly string _connectionString;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

        public AccessCardRestrictionService(ILogger<AccessCardRestrictionService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("DefaultConnection string is missing.");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WorkNest Access Card Restriction Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ReRestrictLapsedPartialPaymentsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during daily access card restriction check.");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("WorkNest Access Card Restriction Service stopping.");
        }

        private async Task ReRestrictLapsedPartialPaymentsAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Executing WN_ReRestrictLapsedPartialPayments...");

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(stoppingToken);

            using var cmd = new SqlCommand("dbo.WN_ReRestrictLapsedPartialPayments", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            int affected = await cmd.ExecuteNonQueryAsync(stoppingToken);

            _logger.LogInformation("Completed access card restriction check. {Count} access card(s) re-restricted.", affected);
        }
    }
}
