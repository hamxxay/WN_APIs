using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

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
        private readonly ServiceSwitchGate _gate; // on/off switch (api/admin/services): off skips the cycle's work

        public AccessCardRestrictionService(ILogger<AccessCardRestrictionService> logger, IConfiguration configuration, IServiceSwitch serviceSwitch)
        {
            _logger = logger;
            _gate = new ServiceSwitchGate(serviceSwitch, BackgroundServiceCatalog.AccessCardRestriction, logger);
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
                    if (await _gate.IsOnAsync(stoppingToken))
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
            using var cmd = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; EXEC dbo.WN_ReRestrictLapsedPartialPayments;", conn);
            cmd.CommandType = CommandType.Text;
            int affected = await cmd.ExecuteNonQueryAsync(stoppingToken);

            _logger.LogInformation("Completed access card restriction check. {Count} access card(s) re-restricted.", affected);
        }
    }
}
