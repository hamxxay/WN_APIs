using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.LoginHistory;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    public class LoginHistoryRepository : ILoginHistoryRepository
    {
        private readonly string _connectionString;
        private readonly IBusinessClock _clock;

        public LoginHistoryRepository(IConfiguration configuration, IBusinessClock clock)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _clock = clock;
        }

        private async Task<SqlConnection> OpenAsync(CancellationToken ct)
        {
            var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            return conn;
        }

        public async Task<LoginHistoryStatsDto> GetStatsAsync(CancellationToken ct = default)
        {
            var stats = new LoginHistoryStatsDto();
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.WN_LoginHistory_GetStats", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                stats.LoginsToday = reader.IsDBNull(reader.GetOrdinal("LoginsToday")) ? 0 : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("LoginsToday")));
                stats.FailedToday = reader.IsDBNull(reader.GetOrdinal("FailedToday")) ? 0 : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("FailedToday")));
                stats.DistinctUsersToday = reader.IsDBNull(reader.GetOrdinal("DistinctUsersToday")) ? 0 : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("DistinctUsersToday")));
            }

            return stats;
        }

        public async Task<LoginHistoryPagedResultDto> GetPagedLoginHistoryAsync(LoginHistoryFilterDto filter, CancellationToken ct = default)
        {
            var page = Math.Max(1, filter.Page);
            var pageSize = Math.Clamp(filter.PageSize > 0 ? filter.PageSize : 50, 1, 200);

            var result = new LoginHistoryPagedResultDto
            {
                Page = page,
                PageSize = pageSize,
                Items = new List<LoginHistoryDto>()
            };

            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.WN_LoginHistory_GetPaged", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.Add(new SqlParameter("@Page", SqlDbType.Int) { Value = page });
            cmd.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });
            cmd.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.DateTime2)
            {
                Value = filter.FromDate.HasValue ? filter.FromDate.Value : DBNull.Value
            });
            cmd.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.DateTime2)
            {
                Value = filter.ToDate.HasValue ? filter.ToDate.Value : DBNull.Value
            });
            cmd.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 256)
            {
                Value = !string.IsNullOrWhiteSpace(filter.Email) ? filter.Email.Trim() : DBNull.Value
            });
            cmd.Parameters.Add(new SqlParameter("@EventType", SqlDbType.VarChar, 10)
            {
                Value = !string.IsNullOrWhiteSpace(filter.EventType) ? filter.EventType.Trim().ToUpperInvariant() : DBNull.Value
            });
            cmd.Parameters.Add(new SqlParameter("@IpAddress", SqlDbType.VarChar, 45)
            {
                Value = !string.IsNullOrWhiteSpace(filter.IpAddress) ? filter.IpAddress.Trim() : DBNull.Value
            });
            cmd.Parameters.Add(new SqlParameter("@LatestOnly", SqlDbType.Bit)
            {
                Value = filter.LatestOnly
            });

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            // Result Set 1: TotalCount
            if (await reader.ReadAsync(ct))
            {
                result.TotalCount = reader.GetInt32(reader.GetOrdinal("TotalCount"));
            }

            // Result Set 2: Paged items
            if (await reader.NextResultAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var eventAtUtc = reader.GetDateTime(reader.GetOrdinal("EventAt"));
                    var eventAtLocal = _clock.FromUtc(eventAtUtc);
                    var userAgent = reader.IsDBNull(reader.GetOrdinal("UserAgent")) ? null : reader.GetString(reader.GetOrdinal("UserAgent"));

                    result.Items.Add(new LoginHistoryDto
                    {
                        LoginId = reader.GetInt64(reader.GetOrdinal("LoginId")),
                        UserId = reader.IsDBNull(reader.GetOrdinal("UserId")) ? null : reader.GetInt32(reader.GetOrdinal("UserId")),
                        Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader.GetString(reader.GetOrdinal("Email")),
                        EventType = reader.GetString(reader.GetOrdinal("EventType")),
                        FailureReason = reader.IsDBNull(reader.GetOrdinal("FailureReason")) ? null : reader.GetString(reader.GetOrdinal("FailureReason")),
                        IpAddress = reader.IsDBNull(reader.GetOrdinal("IpAddress")) ? null : reader.GetString(reader.GetOrdinal("IpAddress")),
                        UserAgent = userAgent,
                        DeviceSummary = UserAgentParser.ParseSummary(userAgent),
                        EventAt = eventAtLocal,
                        EventAtUtc = eventAtUtc
                    });
                }
            }

            // Also load stats
            result.Stats = await GetStatsAsync(ct);

            return result;
        }
    }
}
