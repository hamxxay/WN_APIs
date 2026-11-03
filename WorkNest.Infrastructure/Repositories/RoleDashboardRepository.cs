using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Data for the role-based dashboard sections via stored procedures.
    /// </summary>
    public class RoleDashboardRepository : IRoleDashboardRepository
    {
        private readonly string _connectionString;

        public RoleDashboardRepository(IConfiguration configuration)
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

        public Task<List<List<IDictionary<string, object?>>>> GetSalesAsync(IReadOnlyCollection<int>? locationIds,
            DateTime periodStart, DateTime now, IEnumerable<int> openInvoiceStatusIds, int renewalDays, DateTime? periodEnd = null,
            int utcShiftMinutes = 0) =>
            ExecProcedureAsync("dbo.WN_RoleDashboard_GetSales", p =>
            {
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = locationIds is null ? DBNull.Value : string.Join(",", locationIds.Distinct());
                p.Add("@PStart", SqlDbType.DateTime2).Value = periodStart;
                p.Add("@PEnd", SqlDbType.DateTime2).Value = (object?)periodEnd ?? DBNull.Value;
                p.Add("@UtcShift", SqlDbType.Int).Value = utcShiftMinutes;
                p.Add("@Today", SqlDbType.Date).Value = now.Date;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = now;
                p.Add("@OpenInvoiceStatusIds", SqlDbType.NVarChar, -1).Value = string.Join(",", openInvoiceStatusIds.Distinct());
                p.Add("@RenewalDays", SqlDbType.Int).Value = renewalDays;
            });

        public Task<List<List<IDictionary<string, object?>>>> GetTeamAsync(IReadOnlyCollection<int>? locationIds, DateTime now) =>
            ExecProcedureAsync("dbo.WN_RoleDashboard_GetTeam", p =>
            {
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = locationIds is null ? DBNull.Value : string.Join(",", locationIds.Distinct());
                p.Add("@Today", SqlDbType.Date).Value = now.Date;
            });

        public Task<List<List<IDictionary<string, object?>>>> GetLocationComparisonAsync(DateTime periodStart, DateTime now, IEnumerable<int> openInvoiceStatusIds, IEnumerable<int> voidStatusIds,
            DateTime? periodEnd = null) =>
            ExecProcedureAsync("dbo.WN_RoleDashboard_GetLocationComparison", p =>
            {
                p.Add("@PStart", SqlDbType.DateTime2).Value = periodStart;
                p.Add("@PEnd", SqlDbType.DateTime2).Value = (object?)periodEnd ?? DBNull.Value;
                p.Add("@Today", SqlDbType.Date).Value = now.Date;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = now;
                p.Add("@OpenInvoiceStatusIds", SqlDbType.NVarChar, -1).Value = string.Join(",", openInvoiceStatusIds.Distinct());
                p.Add("@VoidStatusIds", SqlDbType.NVarChar, -1).Value = string.Join(",", voidStatusIds.Distinct());
            });

        public Task<List<List<IDictionary<string, object?>>>> GetStaffAsync(DateTime periodStart, DateTime? periodEnd = null, int utcShiftMinutes = 0) =>
            ExecProcedureAsync("dbo.WN_RoleDashboard_GetStaff", p =>
            {
                p.Add("@PStart", SqlDbType.DateTime2).Value = periodStart;
                p.Add("@PEnd", SqlDbType.DateTime2).Value = (object?)periodEnd ?? DBNull.Value;
                p.Add("@UtcShift", SqlDbType.Int).Value = utcShiftMinutes;
            });

        public Task<List<List<IDictionary<string, object?>>>> GetSystemAsync() =>
            ExecProcedureAsync("dbo.WN_RoleDashboard_GetSystem", _ => { });
    }
}
