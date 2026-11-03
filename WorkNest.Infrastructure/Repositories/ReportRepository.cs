using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Reports;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Data for the revenue &amp; occupancy forecast, weekly super admin report, and run lock via stored procedures.
    /// </summary>
    public class ReportRepository : IReportRepository
    {
        private readonly string _connectionString;

        public ReportRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private async Task<List<List<IDictionary<string, object?>>>> ExecProcedureAsync(string spName, Action<SqlParameterCollection> bind)
        {
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            await using var cmd = new SqlCommand(spName, c) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
            bind(cmd.Parameters);
            await using var r = await cmd.ExecuteReaderAsync();
            var sets = new List<List<IDictionary<string, object?>>>();
            do
            {
                var rows = new List<IDictionary<string, object?>>();
                while (await r.ReadAsync())
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                    rows.Add(row);
                }
                sets.Add(rows);
            } while (await r.NextResultAsync());
            return sets;
        }

        public Task<List<List<IDictionary<string, object?>>>> GetForecastAsync(IReadOnlyCollection<int>? locationIds, DateTime firstMonth, int months) =>
            ExecProcedureAsync("dbo.WN_Reports_GetForecast", p =>
            {
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = locationIds is null ? DBNull.Value : string.Join(",", locationIds.Distinct());
                p.Add("@FirstMonth", SqlDbType.Date).Value = firstMonth.Date;
                p.Add("@Months", SqlDbType.Int).Value = months;
            });

        public Task<List<List<IDictionary<string, object?>>>> GetWeeklyExtrasAsync(DateTime weekStart, DateTime now) =>
            ExecProcedureAsync("dbo.WN_Reports_GetWeeklyExtras", p =>
            {
                p.Add("@WeekStart", SqlDbType.DateTime2).Value = weekStart;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = now;
            });

        public async Task<List<string>> GetSuperAdminEmailsAsync()
        {
            var sets = await ExecProcedureAsync("dbo.WN_Reports_GetSuperAdminEmails", _ => { });
            return sets.Count == 0 ? new List<string>()
                : sets[0].Select(r => r.TryGetValue("Email", out var v) ? Convert.ToString(v) : null)
                         .Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e!).ToList();
        }

        public async Task<ReportClaimResult> TryClaimRunAsync(string reportKey, string periodKey, DateTime at, DateTime retryFailedBefore)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                await using var cmd = new SqlCommand("dbo.WN_Reports_TryClaimRun", c) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
                cmd.Parameters.Add("@Key", SqlDbType.NVarChar, 50).Value = reportKey;
                cmd.Parameters.Add("@Period", SqlDbType.NVarChar, 20).Value = periodKey;
                cmd.Parameters.Add("@At", SqlDbType.DateTime2).Value = at;
                cmd.Parameters.Add("@RetryBefore", SqlDbType.DateTime2).Value = retryFailedBefore;
                var result = Convert.ToString(await cmd.ExecuteScalarAsync());
                return result switch
                {
                    "Claimed" => ReportClaimResult.Claimed,
                    "RetryLater" => ReportClaimResult.RetryLater,
                    _ => ReportClaimResult.Taken
                };
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601) { return ReportClaimResult.Taken; }
            catch (SqlException ex) when (ex.Number == 208) { return ReportClaimResult.TableMissing; }
        }

        public async Task CompleteRunAsync(string reportKey, string periodKey, string status, string recipients, string? error, DateTime at)
        {
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_Reports_CompleteRun", c) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
            cmd.Parameters.Add("@Key", SqlDbType.NVarChar, 50).Value = reportKey;
            cmd.Parameters.Add("@Period", SqlDbType.NVarChar, 20).Value = periodKey;
            cmd.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = status;
            cmd.Parameters.Add("@Recipients", SqlDbType.NVarChar, -1).Value = recipients;
            cmd.Parameters.Add("@Error", SqlDbType.NVarChar, 1000).Value = error is null ? DBNull.Value : (error.Length > 1000 ? error[..1000] : error);
            cmd.Parameters.Add("@At", SqlDbType.DateTime2).Value = at;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
