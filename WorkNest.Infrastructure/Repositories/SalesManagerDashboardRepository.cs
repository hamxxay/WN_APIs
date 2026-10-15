using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Sales Manager dashboard data: calls the read-only stored procedures dbo.WN_SalesMgr_* (script SM_01_sales_manager,
    /// replaced in desks by SM_02_sales_manager_desks). Until SM_01 has run the procedures are missing (SQL error 2812);
    /// until SM_02 has run they answer without their desk columns. Either way the call throws
    /// <see cref="SalesManagerDatabaseUpdateMissingException"/>, which the API answers with 503.
    /// </summary>
    public class SalesManagerDashboardRepository : ISalesManagerDashboardRepository
    {
        private readonly string _connectionString;

        public SalesManagerDashboardRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        protected string ConnectionString => _connectionString;

        private static object Db(object? value) => value ?? DBNull.Value;

        /// <summary>
        /// Runs one procedure and reads every result set (rows as column -> value, NULL as null). <paramref name="deskSet"/>
        /// and <paramref name="deskColumn"/> name a column only the SM_02 version of the procedure returns (checked on the
        /// result set's columns, so also when it has no rows): without it the database still has the SM_01 version.
        /// No <paramref name="deskColumn"/>: a procedure SM_02 did not change.
        /// </summary>
        protected virtual async Task<List<List<IDictionary<string, object?>>>> RunAsync(string procedure, Action<SqlParameterCollection> bind,
            int deskSet = 0, string? deskColumn = null)
        {
            List<List<IDictionary<string, object?>>> sets;
            var columns = new List<HashSet<string>>();
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                await using var cmd = new SqlCommand(procedure, c) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
                bind(cmd.Parameters);
                sets = await ReadSetsAsync(cmd, columns);
            }
            catch (SqlException ex) when (ex.Number == 2812) // Could not find stored procedure: SM_01 not run yet
            {
                throw new SalesManagerDatabaseUpdateMissingException(ex);
            }
            if (deskColumn != null && (deskSet >= columns.Count || !columns[deskSet].Contains(deskColumn))) // SM_01 version: SM_02 not run yet
                throw SalesManagerDatabaseUpdateMissingException.DesksMissing(procedure, deskColumn);
            return sets;
        }

        /// <summary>Reads every result set; <paramref name="columns"/> receives the column names of each one.</summary>
        protected static async Task<List<List<IDictionary<string, object?>>>> ReadSetsAsync(SqlCommand cmd, List<HashSet<string>> columns)
        {
            await using var r = await cmd.ExecuteReaderAsync();
            var sets = new List<List<IDictionary<string, object?>>>();
            do
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < r.FieldCount; i++) names.Add(r.GetName(i));
                columns.Add(names);
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

        // SM_02 marks: the funnel's BookedDesks, the breakdown head's InDesks, the records' Desks.

        public Task<List<List<IDictionary<string, object?>>>> GetDashboardAsync(string? locationIds, string? pickerLocationIds,
            int periodMonths, DateTime businessNow, int expiringDays) =>
            RunAsync("dbo.WN_SalesMgr_Dashboard", p =>
            {
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = Db(locationIds);
                p.Add("@PickerLocationIds", SqlDbType.NVarChar, -1).Value = Db(pickerLocationIds);
                p.Add("@PMonths", SqlDbType.Int).Value = periodMonths;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                p.Add("@ExpiringDays", SqlDbType.Int).Value = expiringDays;
            }, 1, "BookedDesks");

        public Task<List<List<IDictionary<string, object?>>>> GetBreakdownAsync(string metric, string? locationIds, DateTime? monthStart,
            string? inquiryType, int? userId, DateTime businessNow) =>
            RunAsync("dbo.WN_SalesMgr_Breakdown", p =>
            {
                p.Add("@Metric", SqlDbType.NVarChar, 30).Value = metric;
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = Db(locationIds);
                p.Add("@MonthStart", SqlDbType.Date).Value = Db(monthStart?.Date);
                p.Add("@InquiryType", SqlDbType.NVarChar, 20).Value = Db(inquiryType);
                p.Add("@UserId", SqlDbType.Int).Value = Db(userId);
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
            }, 0, "InDesks");

        public Task<List<List<IDictionary<string, object?>>>> GetQuotationRecordsAsync(string? locationIds, DateTime? from, DateTime? to,
            int? userId, string? stage, int top) =>
            RunAsync("dbo.WN_SalesMgr_Records_Quotations", p =>
            {
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = Db(locationIds);
                p.Add("@From", SqlDbType.DateTime2).Value = Db(from);
                p.Add("@To", SqlDbType.DateTime2).Value = Db(to);
                p.Add("@UserId", SqlDbType.Int).Value = Db(userId);
                p.Add("@Stage", SqlDbType.NVarChar, 20).Value = Db(stage);
                p.Add("@Top", SqlDbType.Int).Value = top;
            }, 1, "Desks");

        public Task<List<List<IDictionary<string, object?>>>> GetBookingRecordsAsync(string? locationIds, bool noLocation, DateTime? from,
            DateTime? to, string? statusIds, bool runningOnly, DateTime businessNow, int top) =>
            RunAsync("dbo.WN_SalesMgr_Records_Bookings", p =>
            {
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = Db(locationIds);
                p.Add("@NoLocation", SqlDbType.Bit).Value = noLocation;
                p.Add("@From", SqlDbType.DateTime2).Value = Db(from);
                p.Add("@To", SqlDbType.DateTime2).Value = Db(to);
                p.Add("@StatusIds", SqlDbType.NVarChar, 100).Value = Db(statusIds);
                p.Add("@RunningOnly", SqlDbType.Bit).Value = runningOnly;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                p.Add("@Top", SqlDbType.Int).Value = top;
            }, 1, "Desks");

        public Task<List<List<IDictionary<string, object?>>>> GetInquiryRecordsAsync(DateTime? from, DateTime? to, string? inquiryType,
            string? outcome, string? reason, DateTime businessNow, int top) =>
            RunAsync("dbo.WN_SalesMgr_Records_Inquiries", p =>
            {
                p.Add("@From", SqlDbType.DateTime2).Value = Db(from);
                p.Add("@To", SqlDbType.DateTime2).Value = Db(to);
                p.Add("@InquiryType", SqlDbType.NVarChar, 20).Value = Db(inquiryType);
                p.Add("@Outcome", SqlDbType.NVarChar, 30).Value = Db(outcome);
                p.Add("@Reason", SqlDbType.NVarChar, 1000).Value = Db(reason);
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                p.Add("@Top", SqlDbType.Int).Value = top;
            });

        public Task<List<List<IDictionary<string, object?>>>> GetLeaseRecordsAsync(string? locationIds, bool noLocation, DateTime? from,
            DateTime? to, int expiringDays, DateTime businessNow, int top) =>
            RunAsync("dbo.WN_SalesMgr_Records_Leases", p =>
            {
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = Db(locationIds);
                p.Add("@NoLocation", SqlDbType.Bit).Value = noLocation;
                p.Add("@From", SqlDbType.DateTime2).Value = Db(from);
                p.Add("@To", SqlDbType.DateTime2).Value = Db(to);
                p.Add("@ExpiringDays", SqlDbType.Int).Value = expiringDays;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                p.Add("@Top", SqlDbType.Int).Value = top;
            }, 1, "Desks");
    }
}
