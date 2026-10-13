using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    public class LoginHistoryService : ILoginHistoryService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly string _connectionString;
        private readonly ILogger<LoginHistoryService> _logger;

        public LoginHistoryService(
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration,
            ILogger<LoginHistoryService> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
        }

        public async Task Record(string eventType, int? userId, string? email, string? failureReason)
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;

                // Capture IP from HttpContext (respecting forwarded headers if resolved/configured)
                string? ip = httpContext?.Connection?.RemoteIpAddress?.ToString();
                if (string.IsNullOrWhiteSpace(ip) && httpContext != null && httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var xff))
                {
                    ip = xff.ToString().Split(',')[0].Trim();
                }
                if (ip != null && ip.Length > 45)
                {
                    ip = ip.Substring(0, 45);
                }

                // Capture User-Agent (truncate to 400 chars)
                string? userAgent = httpContext?.Request.Headers.UserAgent.ToString();
                if (string.IsNullOrWhiteSpace(userAgent) && httpContext != null && httpContext.Request.Headers.TryGetValue("User-Agent", out var ua))
                {
                    userAgent = ua.ToString();
                }
                if (userAgent != null && userAgent.Length > 400)
                {
                    userAgent = userAgent.Substring(0, 400);
                }

                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand("dbo.WN_LoginHistory_Insert", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = (object?)userId ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 256) { Value = (object?)email?.Trim() ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@EventType", SqlDbType.VarChar, 10) { Value = eventType.Trim().ToUpperInvariant() });
                cmd.Parameters.Add(new SqlParameter("@FailureReason", SqlDbType.NVarChar, 100) { Value = (object?)failureReason?.Trim() ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@IpAddress", SqlDbType.VarChar, 45) { Value = (object?)ip ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@UserAgent", SqlDbType.NVarChar, 400) { Value = (object?)userAgent ?? DBNull.Value });

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                // Must never fail or block the login result
                _logger.LogError(ex, "Failed to record login history event {EventType} for {Email}", eventType, email);
            }
        }
    }
}
