using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Main admin dashboard data: calls the read-only stored procedures dbo.WN_Dash_* (script DASH_01_main_dashboard).
    /// Until DASH_01 has run they are missing (SQL error 2812, or 208 for the functions they read) and every call throws
    /// <see cref="MainDashboardDatabaseUpdateMissingException"/>, which the API answers with 503 and the script name.
    /// </summary>
    public class MainDashboardRepository : IMainDashboardRepository
    {
        private readonly string _connectionString;

        public MainDashboardRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private static object Db(object? value) => value ?? DBNull.Value;

        /// <summary>null = every location; an empty list matches nothing (-1). Ints from the API's scope, never user text.</summary>
        private static object Locations(IReadOnlyCollection<int>? ids)
        {
            if (ids is null) return DBNull.Value;
            var list = ids.Distinct().ToList();
            return list.Count == 0 ? "-1" : string.Join(",", list);
        }

        /// <summary>Runs one procedure and reads every result set (rows as column -> value, NULL as null).</summary>
        private async Task<List<List<IDictionary<string, object?>>>> RunAsync(string procedure, Action<SqlParameterCollection> bind)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                await using var cmd = new SqlCommand(procedure, c) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
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
            catch (SqlException ex) when (ex.Number is 2812 or 208) // procedure / function missing: DASH_01 not run yet
            {
                throw new MainDashboardDatabaseUpdateMissingException(ex);
            }
        }

        public Task<List<List<IDictionary<string, object?>>>> GetSpaceTypeSummaryAsync(IReadOnlyCollection<int>? locationIds, int expiringDays,
            DateTime businessNow) =>
            RunAsync("dbo.WN_Dash_SpaceTypeSummary", p =>
            {
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = Locations(locationIds);
                p.Add("@ExpiringDays", SqlDbType.Int).Value = expiringDays;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
            });

        public Task<List<List<IDictionary<string, object?>>>> GetSpaceTypeDetailsAsync(IReadOnlyCollection<int>? locationIds, int spaceTypeId,
            string tag, int expiringDays, int page, int pageSize, DateTime businessNow) =>
            RunAsync("dbo.WN_Dash_SpaceTypeDetails", p =>
            {
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = Locations(locationIds);
                p.Add("@SpaceTypeId", SqlDbType.Int).Value = spaceTypeId;
                p.Add("@Tag", SqlDbType.NVarChar, 20).Value = tag;
                p.Add("@ExpiringDays", SqlDbType.Int).Value = expiringDays;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                p.Add("@Page", SqlDbType.Int).Value = page;
                p.Add("@PageSize", SqlDbType.Int).Value = pageSize;
            });

        public Task<List<List<IDictionary<string, object?>>>> GetInventoryAsync(IReadOnlyCollection<int>? locationIds, int? locationId,
            string? search, string? customer, string? spaceType, string? status, bool privateOnly, int expiringDays, string sortBy,
            bool sortDesc, int page, int pageSize, DateTime businessNow) =>
            RunAsync("dbo.WN_Dash_SpaceInventory", p =>
            {
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = Locations(locationIds);
                p.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                p.Add("@Search", SqlDbType.NVarChar, 200).Value = Db(search);
                p.Add("@Customer", SqlDbType.NVarChar, 200).Value = Db(customer);
                p.Add("@SpaceType", SqlDbType.NVarChar, 200).Value = Db(spaceType);
                p.Add("@Status", SqlDbType.NVarChar, 20).Value = Db(status);
                p.Add("@PrivateOnly", SqlDbType.Bit).Value = privateOnly;
                p.Add("@ExpiringDays", SqlDbType.Int).Value = expiringDays;
                p.Add("@SortBy", SqlDbType.NVarChar, 20).Value = sortBy;
                p.Add("@SortDesc", SqlDbType.Bit).Value = sortDesc;
                p.Add("@Page", SqlDbType.Int).Value = page;
                p.Add("@PageSize", SqlDbType.Int).Value = pageSize;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
            });
    }
}
