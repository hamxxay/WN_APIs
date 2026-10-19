using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Sales Manager dashboard data: calls the read-only stored procedures dbo.WN_SalesMgr_* (script SM_01_sales_manager,
    /// replaced in desks by SM_02_sales_manager_desks; the complaints ones from SM_03_sales_manager_complaints; tour
    /// inquiries by branch from BR_02_sm_inquiries_branch). Until SM_01 (complaints: SM_03) has run the procedures are
    /// missing (SQL error 2812); until SM_02 has run they answer without their desk columns. Either way the call throws
    /// <see cref="SalesManagerDatabaseUpdateMissingException"/>, which the API answers with 503. Until BR_02 has run the
    /// procedures do not know the inquiry branch parameters (SQL error 8144, too many arguments): the call is repeated
    /// without them and the tour inquiries of every branch are counted, as before. A custom date range
    /// (CS_01_ceo_sales_dates: @From / @To) is sent only when one is asked for, so the calendar periods keep working with
    /// the procedures from before CS_01; with a range, 8144 / 8145 means CS_01 has not run
    /// (<see cref="SalesManagerDatabaseUpdateMissingException.CustomRangeMissing"/>).
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

        /// <summary>
        /// Runs a procedure with the BR_02 tour inquiry branch parameters (<paramref name="branch"/>). A database without
        /// BR_02 answers 8144 (too many arguments): run it again without them (every branch's inquiries, as before).
        /// </summary>
        private async Task<List<List<IDictionary<string, object?>>>> RunWithInquiryBranchAsync(string procedure,
            Action<SqlParameterCollection> bind, Action<SqlParameterCollection> branch, int deskSet = 0, string? deskColumn = null,
            DateTime? from = null, DateTime? to = null)
        {
            // A custom range: no fallback without the branch parameters (the CS_01 procedures have both).
            if (from != null && to != null)
                return await RunWithRangeAsync(procedure, p => { bind(p); branch(p); }, from, to, deskSet, deskColumn);
            try
            {
                return await RunAsync(procedure, p => { bind(p); branch(p); }, deskSet, deskColumn);
            }
            catch (SqlException ex) when (ex.Number is 8144 or 8145) // BR_02 not run yet: no inquiry branch parameters
            {
                return await RunAsync(procedure, bind, deskSet, deskColumn);
            }
        }

        /// <summary>
        /// Runs a procedure, with @From / @To (days, both included) only when both are given (CS_01). Without them the
        /// call is exactly the one from before CS_01. With them, 8144 / 8145 (too many arguments / not a parameter): the
        /// procedure is still the one from before CS_01.
        /// </summary>
        private async Task<List<List<IDictionary<string, object?>>>> RunWithRangeAsync(string procedure, Action<SqlParameterCollection> bind,
            DateTime? from, DateTime? to, int deskSet = 0, string? deskColumn = null)
        {
            if (from == null || to == null) return await RunAsync(procedure, bind, deskSet, deskColumn);
            try
            {
                return await RunAsync(procedure, p =>
                {
                    bind(p);
                    p.Add("@From", SqlDbType.Date).Value = from.Value.Date;
                    p.Add("@To", SqlDbType.Date).Value = to.Value.Date;
                }, deskSet, deskColumn);
            }
            catch (SqlException ex) when (ex.Number is 8144 or 8145) // CS_01 not run yet: no @From / @To
            {
                throw SalesManagerDatabaseUpdateMissingException.CustomRangeMissing(procedure, ex.Message);
            }
        }

        // SM_02 marks: the funnel's BookedDesks, the breakdown head's InDesks, the records' Desks.
        // BR_02 marks: the inquiries row's ByBranch, the breakdown head's InqByBranch, the inquiry records' ByBranch.

        public Task<List<List<IDictionary<string, object?>>>> GetDashboardAsync(string? locationIds, string? pickerLocationIds,
            bool inquiriesNoBranch, int periodMonths, DateTime businessNow, int expiringDays, DateTime? from = null, DateTime? to = null) =>
            RunWithInquiryBranchAsync("dbo.WN_SalesMgr_Dashboard", p =>
            {
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = Db(locationIds);
                p.Add("@PickerLocationIds", SqlDbType.NVarChar, -1).Value = Db(pickerLocationIds);
                p.Add("@PMonths", SqlDbType.Int).Value = periodMonths;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                p.Add("@ExpiringDays", SqlDbType.Int).Value = expiringDays;
            }, p => p.Add("@InquiriesNoBranch", SqlDbType.Bit).Value = inquiriesNoBranch, 1, "BookedDesks", from, to);

        public Task<List<List<IDictionary<string, object?>>>> GetBreakdownAsync(string metric, string? locationIds, bool inquiriesNoBranch,
            DateTime? monthStart, string? inquiryType, int? userId, DateTime businessNow) =>
            RunWithInquiryBranchAsync("dbo.WN_SalesMgr_Breakdown", p =>
            {
                p.Add("@Metric", SqlDbType.NVarChar, 30).Value = metric;
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = Db(locationIds);
                p.Add("@MonthStart", SqlDbType.Date).Value = Db(monthStart?.Date);
                p.Add("@InquiryType", SqlDbType.NVarChar, 20).Value = Db(inquiryType);
                p.Add("@UserId", SqlDbType.Int).Value = Db(userId);
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
            }, p => p.Add("@InquiriesNoBranch", SqlDbType.Bit).Value = inquiriesNoBranch, 0, "InDesks");

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

        public Task<List<List<IDictionary<string, object?>>>> GetInquiryRecordsAsync(string? locationIds, bool includeNoBranch, DateTime? from,
            DateTime? to, string? inquiryType, string? outcome, string? reason, DateTime businessNow, int top) =>
            RunWithInquiryBranchAsync("dbo.WN_SalesMgr_Records_Inquiries", p =>
            {
                p.Add("@From", SqlDbType.DateTime2).Value = Db(from);
                p.Add("@To", SqlDbType.DateTime2).Value = Db(to);
                p.Add("@InquiryType", SqlDbType.NVarChar, 20).Value = Db(inquiryType);
                p.Add("@Outcome", SqlDbType.NVarChar, 30).Value = Db(outcome);
                p.Add("@Reason", SqlDbType.NVarChar, 1000).Value = Db(reason);
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                p.Add("@Top", SqlDbType.Int).Value = top;
            }, p =>
            {
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = Db(locationIds);
                p.Add("@IncludeNoLocation", SqlDbType.Bit).Value = includeNoBranch;
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

        // Complaints section (SM_03): a missing procedure means SM_03 has not run (whatever SM_01 / SM_02 did).

        public Task<List<List<IDictionary<string, object?>>>> GetComplaintsAsync(string? locationIds, bool includeNoBranch, int periodMonths,
            DateTime businessNow, int oldestTop, DateTime? from = null, DateTime? to = null) =>
            RunComplaintsAsync("dbo.WN_SalesMgr_Complaints", p =>
            {
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = Db(locationIds);
                p.Add("@NoBranch", SqlDbType.Bit).Value = includeNoBranch;
                p.Add("@PMonths", SqlDbType.Int).Value = periodMonths;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                p.Add("@OldestTop", SqlDbType.Int).Value = oldestTop;
            }, from, to);

        public Task<List<List<IDictionary<string, object?>>>> GetComplaintRecordsAsync(string? locationIds, bool includeNoBranch,
            string? status, string? category, string? branch, DateTime? month, int periodMonths, DateTime businessNow, int top,
            DateTime? from = null, DateTime? to = null) =>
            RunComplaintsAsync("dbo.WN_SalesMgr_Records_Complaints", p =>
            {
                p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = Db(locationIds);
                p.Add("@NoBranch", SqlDbType.Bit).Value = includeNoBranch;
                p.Add("@Status", SqlDbType.NVarChar, 30).Value = Db(status);
                p.Add("@Category", SqlDbType.NVarChar, 100).Value = Db(category);
                p.Add("@Branch", SqlDbType.NVarChar, 100).Value = Db(branch);
                p.Add("@Month", SqlDbType.Date).Value = Db(month?.Date);
                p.Add("@PMonths", SqlDbType.Int).Value = periodMonths;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                p.Add("@Top", SqlDbType.Int).Value = top;
            }, from, to);

        private async Task<List<List<IDictionary<string, object?>>>> RunComplaintsAsync(string procedure, Action<SqlParameterCollection> bind,
            DateTime? from = null, DateTime? to = null)
        {
            try
            {
                return await RunWithRangeAsync(procedure, bind, from, to);
            }
            catch (SalesManagerDatabaseUpdateMissingException ex) when (ex.Update == "SM_01") // procedure missing (2812)
            {
                throw SalesManagerDatabaseUpdateMissingException.ComplaintsMissing(procedure, ex.Detail);
            }
        }
    }
}
