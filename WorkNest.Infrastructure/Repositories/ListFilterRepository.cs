using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Lists;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// The read-only LST_01 list procedures (script LST_01_list_filters): dbo.WN_Bookings_GetListFiltered,
    /// dbo.WN_Quotations_GetListFiltered, dbo.WN_GetInvoicesListFiltered, dbo.WN_Payments_GetListFiltered,
    /// dbo.WN_Customers_GetListFiltered and dbo.WN_ListStatusOptions_Get. Rows are read like DbRepository reads the old
    /// list procedures (DBNull = null, UTC audit columns marked UTC), so the JSON is the same. SQL errors 2812 / 8144 /
    /// 8145 (procedure missing, parameters unknown) mean LST_01 has not run: <see cref="ListFiltersDatabaseUpdateMissingException"/>.
    /// </summary>
    public class ListFilterRepository : IListFilterRepository
    {
        private readonly string _connectionString;

        public ListFilterRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        public Task<(List<IDictionary<string, object?>> Rows, int Total)> GetBookingsAsync(int page, int limit, string? search, int? locationId, string? statusIds, ListDateRange startDate) =>
            ReadPageAsync("dbo.WN_Bookings_GetListFiltered", p =>
            {
                Paging(p, page, limit);
                p.Add("@Search", SqlDbType.NVarChar, 255).Value = (object?)search ?? DBNull.Value;
                p.Add("@LocationId", SqlDbType.Int).Value = (object?)locationId ?? DBNull.Value;
                p.Add("@StatusIds", SqlDbType.NVarChar, 400).Value = (object?)statusIds ?? DBNull.Value;
                Dates(p, startDate);
            });

        public Task<(List<IDictionary<string, object?>> Rows, int Total)> GetQuotationsAsync(int page, int limit, string? search, int? locationId, string? status, ListDateRange quotationDate) =>
            ReadPageAsync("dbo.WN_Quotations_GetListFiltered", p =>
            {
                Paging(p, page, limit);
                p.Add("@Search", SqlDbType.NVarChar, 100).Value = (object?)search ?? DBNull.Value;
                p.Add("@LocationId", SqlDbType.Int).Value = (object?)locationId ?? DBNull.Value;
                p.Add("@Status", SqlDbType.NVarChar, 50).Value = (object?)status ?? DBNull.Value;
                Dates(p, quotationDate);
            });

        public Task<(List<IDictionary<string, object?>> Rows, int Total)> GetInvoicesAsync(int page, int limit, string? search, int? typeId, int? locationId, string? status, DateTime today, ListDateRange issueDate) =>
            ReadPageAsync("dbo.WN_GetInvoicesListFiltered", p =>
            {
                Paging(p, page, limit);
                p.Add("@Search", SqlDbType.NVarChar, 200).Value = (object?)search ?? DBNull.Value;
                p.Add("@TypeId", SqlDbType.Int).Value = (object?)typeId ?? DBNull.Value;
                p.Add("@LocationId", SqlDbType.Int).Value = (object?)locationId ?? DBNull.Value;
                p.Add("@Status", SqlDbType.NVarChar, 20).Value = (object?)status ?? DBNull.Value;
                p.Add("@Today", SqlDbType.Date).Value = today.Date;
                Dates(p, issueDate);
            });

        public Task<(List<IDictionary<string, object?>> Rows, int Total)> GetPaymentsAsync(int page, int limit, string? search, string? locationIds, int? statusId, ListDateRange paymentDateUtc) =>
            ReadPageAsync("dbo.WN_Payments_GetListFiltered", p =>
            {
                Paging(p, page, limit);
                p.Add("@Search", SqlDbType.NVarChar, 255).Value = (object?)search ?? DBNull.Value;
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = (object?)locationIds ?? DBNull.Value;
                p.Add("@StatusId", SqlDbType.Int).Value = (object?)statusId ?? DBNull.Value;
                Dates(p, paymentDateUtc);
            });

        public Task<(List<IDictionary<string, object?>> Rows, int Total)> GetCustomersAsync(int page, int limit, string? search, string? locationIds, bool? isActive, ListDateRange createdUtc) =>
            ReadPageAsync("dbo.WN_Customers_GetListFiltered", p =>
            {
                Paging(p, page, limit);
                p.Add("@Search", SqlDbType.NVarChar, 255).Value = (object?)search ?? DBNull.Value;
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = (object?)locationIds ?? DBNull.Value;
                p.Add("@IsActive", SqlDbType.Bit).Value = (object?)isActive ?? DBNull.Value;
                Dates(p, createdUtc);
            });

        public async Task<List<ListStatusOption>> GetStatusOptionsAsync(string list, DateTime today)
        {
            var (rows, _) = await ReadPageAsync("dbo.WN_ListStatusOptions_Get", p =>
            {
                p.Add("@List", SqlDbType.NVarChar, 20).Value = list;
                p.Add("@Today", SqlDbType.Date).Value = today.Date;
            });
            return rows
                .Select(r => new ListStatusOption
                {
                    Value = Convert.ToString(r.TryGetValue("Value", out var v) ? v : null)?.Trim() ?? "",
                    Label = Convert.ToString(r.TryGetValue("Label", out var l) ? l : null)?.Trim() ?? ""
                })
                .Where(o => o.Value.Length > 0 && o.Label.Length > 0)
                .ToList();
        }

        private static void Paging(SqlParameterCollection p, int page, int limit)
        {
            p.Add("@Page", SqlDbType.Int).Value = page;
            p.Add("@Limit", SqlDbType.Int).Value = limit;
        }

        private static void Dates(SqlParameterCollection p, ListDateRange range)
        {
            p.Add("@DateFrom", SqlDbType.DateTime2).Value = (object?)range.Start ?? DBNull.Value;
            p.Add("@DateTo", SqlDbType.DateTime2).Value = (object?)range.End ?? DBNull.Value;
        }

        /// <summary>Runs a list procedure; Total = TotalCount of the first row (the row count when there is none).</summary>
        private async Task<(List<IDictionary<string, object?>> Rows, int Total)> ReadPageAsync(string procedure, Action<SqlParameterCollection> parameters)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                await using var cmd = new SqlCommand(procedure, c) { CommandType = CommandType.StoredProcedure };
                parameters(cmd.Parameters);
                var rows = new List<IDictionary<string, object?>>();
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var d = new Dictionary<string, object?>(r.FieldCount, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < r.FieldCount; i++)
                    {
                        var name = r.GetName(i);
                        d[name] = r.IsDBNull(i) ? null : DbDateTimeKinds.Normalize(name, r.GetValue(i));
                    }
                    rows.Add(d);
                }
                int total = rows.Count > 0 && rows[0].TryGetValue("TotalCount", out var t) && t != null ? Convert.ToInt32(t) : rows.Count;
                return (rows, total);
            }
            catch (SqlException ex) when (ex.Number is 2812 or 8144 or 8145) // procedure missing / parameters unknown: LST_01 not run
            {
                throw new ListFiltersDatabaseUpdateMissingException(ex);
            }
        }
    }
}
