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

        public async Task<bool> HasSignedInBeforeAsync(int userId, string? email, DateTime beforeUtc)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                // UserId uses IX_WN_LoginHistory_UserId_LoginId; the email match only covers a first sign-in that created the user row.
                // EventAt is UTC (WN_LoginHistory_Insert uses SYSUTCDATETIME()).
                await using var cmd = new SqlCommand(
                    "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.WN_LoginHistory WITH (NOLOCK) WHERE UserId = @UserId AND EventType = 'LOGIN' AND EventAt < @BeforeUtc) " +
                    "OR EXISTS (SELECT 1 FROM dbo.WN_LoginHistory WITH (NOLOCK) WHERE UserId IS NULL AND @Email IS NOT NULL AND Email = @Email AND EventType = 'LOGIN' AND EventAt < @BeforeUtc) " +
                    "THEN 1 ELSE 0 END", conn);
                cmd.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });
                cmd.Parameters.Add(new SqlParameter("@BeforeUtc", SqlDbType.DateTime2) 
                {
                    Value = DateTime.SpecifyKind(beforeUtc.Kind == DateTimeKind.Local ? beforeUtc.ToUniversalTime() : beforeUtc, DateTimeKind.Unspecified)
                });
                cmd.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 256)
                {
                    Value = string.IsNullOrWhiteSpace(email) ? DBNull.Value : email.Trim()
                });
                return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
            }
            catch (Exception ex)
            {
                // Fails closed: an unverified Firebase email is then refused until it is verified.
                _logger.LogError(ex, "Could not read the login history for user {UserId}", userId);
                return false;
            }
        }
    }
}
