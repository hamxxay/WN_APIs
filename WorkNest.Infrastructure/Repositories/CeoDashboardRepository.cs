using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// CEO dashboard data: calls the read-only stored procedures dbo.WN_Ceo_* (script CEO_01_dashboard_procedures,
    /// changed by QA_07_dashboards). Until CEO_01 has run the procedures are missing (SQL error 2812) and every call
    /// throws <see cref="CeoDatabaseUpdateMissingException"/>, which the API answers with 503 (the full leases list:
    /// naming QA_07; the room report: naming CEO_02, which api/ceo/rooms answers with available = false). Until QA_07
    /// has run the procedures do not know the tour inquiry location parameters (SQL error 8144 / 8145, too many
    /// arguments): the call is repeated without them and every location's inquiries count, as before. A custom date
    /// range (CS_01_ceo_sales_dates: @From / @To) is sent only when one is asked for, so the calendar periods keep
    /// working with the procedures from before CS_01; with a range, 8144 / 8145 means CS_01 has not run
    /// (<see cref="CeoDatabaseUpdateMissingException.CustomRangeMissing"/>).
    /// </summary>
    public class CeoDashboardRepository : ICeoDashboardRepository
    {
        private readonly string _connectionString;

        public CeoDashboardRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private static string Csv(IEnumerable<int> ids) => string.Join(",", ids.Distinct());

        private static void AddStatuses(SqlParameterCollection p, CeoInvoiceStatusIds st)
        {
            p.Add("@OpenStatusIds", SqlDbType.NVarChar, 400).Value = Csv(st.Open);
            p.Add("@PaidStatusIds", SqlDbType.NVarChar, 400).Value = Csv(st.Paid);
            p.Add("@VoidStatusIds", SqlDbType.NVarChar, 400).Value = Csv(st.Void);
        }

        private static object Db(object? value) => value ?? DBNull.Value;

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
            catch (SqlException ex) when (ex.Number == 2812) // Could not find stored procedure: CEO_01 not run yet
            {
                throw new CeoDatabaseUpdateMissingException(ex);
            }
        }

        /// <summary>
        /// Runs a procedure with the QA_07 tour inquiry location parameters (<paramref name="extra"/>). A database without
        /// QA_07 answers 8144 / 8145 (too many arguments / not a parameter): run it again without them (as before).
        /// With a custom range (<paramref name="from"/> / <paramref name="to"/>) there is no such fallback: the CS_01
        /// procedures have both, so 8144 / 8145 then means CS_01 has not run.
        /// </summary>
        private async Task<List<List<IDictionary<string, object?>>>> RunWithInquiryLocationAsync(string procedure,
            Action<SqlParameterCollection> bind, Action<SqlParameterCollection> extra, DateTime? from = null, DateTime? to = null)
        {
            if (from != null && to != null)
                return await RunWithRangeAsync(procedure, p => { bind(p); extra(p); }, from, to);
            try
            {
                return await RunAsync(procedure, p => { bind(p); extra(p); });
            }
            catch (SqlException ex) when (ex.Number is 8144 or 8145) // QA_07 not run yet: no inquiry location parameters
            {
                return await RunAsync(procedure, bind);
            }
        }

        /// <summary>
        /// Runs a procedure, with @From / @To (days, both included) only when both are given (CS_01). Without them the
        /// call is exactly the one from before CS_01. With them, 8144 / 8145 (too many arguments / not a parameter):
        /// the procedure is still the one from before CS_01.
        /// </summary>
        private async Task<List<List<IDictionary<string, object?>>>> RunWithRangeAsync(string procedure, Action<SqlParameterCollection> bind,
            DateTime? from, DateTime? to)
        {
            if (from == null || to == null) return await RunAsync(procedure, bind);
            try
            {
                return await RunAsync(procedure, p =>
                {
                    bind(p);
                    p.Add("@From", SqlDbType.Date).Value = from.Value.Date;
                    p.Add("@To", SqlDbType.Date).Value = to.Value.Date;
                });
            }
            catch (SqlException ex) when (ex.Number is 8144 or 8145) // CS_01 not run yet: no @From / @To
            {
                throw CeoDatabaseUpdateMissingException.CustomRangeMissing(ex);
            }
        }

        // QA_07 marks: the sales funnel's InqByLocation, the breakdown head's InqByLocation, the inquiry records' ByLocation
        // and the dashboard's result set [13] (all leases expiring).

        public Task<List<List<IDictionary<string, object?>>>> GetDashboardAsync(int? locationId, int periodMonths, DateTime businessNow,
            CeoInvoiceStatusIds statuses, int expiringDays, DateTime? from = null, DateTime? to = null) =>
            RunWithInquiryLocationAsync("dbo.WN_Ceo_Dashboard", p =>
            {
                p.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                p.Add("@PMonths", SqlDbType.Int).Value = periodMonths;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                AddStatuses(p, statuses);
                p.Add("@ExpiringDays", SqlDbType.Int).Value = expiringDays;
            }, p => p.Add("@InquiriesByLocation", SqlDbType.Bit).Value = true, from, to);

        public Task<List<List<IDictionary<string, object?>>>> GetBreakdownAsync(string metric, DateTime? monthStart, string? bucket,
            int? spaceTypeId, int? locationId, string? inquiryType, int? userId, DateTime businessNow, CeoInvoiceStatusIds statuses,
            DateTime? from = null, DateTime? to = null) =>
            RunWithInquiryLocationAsync("dbo.WN_Ceo_Breakdown", p =>
            {
                p.Add("@Metric", SqlDbType.NVarChar, 30).Value = metric;
                p.Add("@MonthStart", SqlDbType.Date).Value = Db(monthStart?.Date);
                p.Add("@Bucket", SqlDbType.NVarChar, 20).Value = Db(bucket);
                p.Add("@SpaceTypeId", SqlDbType.Int).Value = Db(spaceTypeId);
                p.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                p.Add("@InquiryType", SqlDbType.NVarChar, 20).Value = Db(inquiryType);
                p.Add("@UserId", SqlDbType.Int).Value = Db(userId);
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                AddStatuses(p, statuses);
            }, p => p.Add("@InquiriesByLocation", SqlDbType.Bit).Value = true, from, to);

        public Task<List<List<IDictionary<string, object?>>>> GetInvoiceRecordsAsync(DateTime? monthStart, int? locationId, bool noLocation,
            string? bucket, int? customerId, string? status, DateTime businessNow, CeoInvoiceStatusIds statuses, int top,
            DateTime? from = null, DateTime? to = null) =>
            RunWithRangeAsync("dbo.WN_Ceo_Records_Invoices", p =>
            {
                p.Add("@MonthStart", SqlDbType.Date).Value = Db(monthStart?.Date);
                p.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                p.Add("@NoLocation", SqlDbType.Bit).Value = noLocation;
                p.Add("@Bucket", SqlDbType.NVarChar, 20).Value = Db(bucket);
                p.Add("@CustomerId", SqlDbType.Int).Value = Db(customerId);
                p.Add("@Status", SqlDbType.NVarChar, 20).Value = Db(status);
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                AddStatuses(p, statuses);
                p.Add("@Top", SqlDbType.Int).Value = top;
            }, from, to);

        public Task<List<List<IDictionary<string, object?>>>> GetSpaceRecordsAsync(int? spaceTypeId, int? locationId, bool noLocation,
            bool? occupied, DateTime businessNow, int top) =>
            RunAsync("dbo.WN_Ceo_Records_Spaces", p =>
            {
                p.Add("@SpaceTypeId", SqlDbType.Int).Value = Db(spaceTypeId);
                p.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                p.Add("@NoLocation", SqlDbType.Bit).Value = noLocation;
                p.Add("@Occupied", SqlDbType.Bit).Value = Db(occupied);
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                p.Add("@Top", SqlDbType.Int).Value = top;
            });

        public Task<List<List<IDictionary<string, object?>>>> GetBookingRecordsAsync(DateTime? from, DateTime? to, int? locationId, bool noLocation,
            string? statusIds, bool runningOnly, DateTime businessNow, int top) =>
            RunAsync("dbo.WN_Ceo_Records_Bookings", p =>
            {
                p.Add("@From", SqlDbType.DateTime2).Value = Db(from);
                p.Add("@To", SqlDbType.DateTime2).Value = Db(to);
                p.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                p.Add("@NoLocation", SqlDbType.Bit).Value = noLocation;
                p.Add("@StatusIds", SqlDbType.NVarChar, 100).Value = Db(statusIds);
                p.Add("@RunningOnly", SqlDbType.Bit).Value = runningOnly;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                p.Add("@Top", SqlDbType.Int).Value = top;
            });

        public Task<List<List<IDictionary<string, object?>>>> GetInquiryRecordsAsync(DateTime? from, DateTime? to, string? inquiryType,
            int? locationId, int top)
        {
            void Bind(SqlParameterCollection p)
            {
                p.Add("@From", SqlDbType.DateTime2).Value = Db(from);
                p.Add("@To", SqlDbType.DateTime2).Value = Db(to);
                p.Add("@InquiryType", SqlDbType.NVarChar, 20).Value = Db(inquiryType);
                p.Add("@Top", SqlDbType.Int).Value = top;
            }
            // Every location: no branch filter to ask for (every inquiry, before and after QA_07).
            return locationId is > 0
                ? RunWithInquiryLocationAsync("dbo.WN_Ceo_Records_Inquiries", Bind, p => p.Add("@LocationId", SqlDbType.Int).Value = locationId.Value)
                : RunAsync("dbo.WN_Ceo_Records_Inquiries", Bind);
        }

        public Task<List<List<IDictionary<string, object?>>>> GetQuotationRecordsAsync(DateTime? from, DateTime? to, int? userId, string? stage,
            int? locationId, int top) =>
            RunAsync("dbo.WN_Ceo_Records_Quotations", p =>
            {
                p.Add("@From", SqlDbType.DateTime2).Value = Db(from);
                p.Add("@To", SqlDbType.DateTime2).Value = Db(to);
                p.Add("@UserId", SqlDbType.Int).Value = Db(userId);
                p.Add("@Stage", SqlDbType.NVarChar, 20).Value = Db(stage);
                p.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                p.Add("@Top", SqlDbType.Int).Value = top;
            });

        public async Task<List<List<IDictionary<string, object?>>>> GetSalesRecordsAsync(DateTime? from, DateTime? to, int? locationId,
            int? sellerId, int top)
        {
            try
            {
                return await RunAsync("dbo.WN_Ceo_Records_Sales", p =>
                {
                    p.Add("@From", SqlDbType.DateTime2).Value = Db(from);
                    p.Add("@To", SqlDbType.DateTime2).Value = Db(to);
                    p.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                    p.Add("@SellerId", SqlDbType.Int).Value = Db(sellerId);
                    p.Add("@Top", SqlDbType.Int).Value = top;
                });
            }
            catch (CeoDatabaseUpdateMissingException ex) when (ex.Update == "CEO_01") // this procedure comes with CP_02, not CEO_01
            {
                throw CeoDatabaseUpdateMissingException.SalesRecordsMissing(ex.InnerException);
            }
        }

        public async Task<List<List<IDictionary<string, object?>>>> GetLeaseRecordsAsync(int? locationId, bool noLocation, DateTime? from,
            DateTime? to, int expiringDays, DateTime businessNow, int top)
        {
            try
            {
                return await RunAsync("dbo.WN_Ceo_Records_Leases", p =>
                {
                    p.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                    p.Add("@NoLocation", SqlDbType.Bit).Value = noLocation;
                    p.Add("@From", SqlDbType.DateTime2).Value = Db(from);
                    p.Add("@To", SqlDbType.DateTime2).Value = Db(to);
                    p.Add("@ExpiringDays", SqlDbType.Int).Value = expiringDays;
                    p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                    p.Add("@Top", SqlDbType.Int).Value = top;
                });
            }
            catch (CeoDatabaseUpdateMissingException ex) when (ex.Update == "CEO_01") // this procedure comes with QA_07, not CEO_01
            {
                throw CeoDatabaseUpdateMissingException.LeasesMissing(ex.InnerException);
            }
        }

        public async Task<List<List<IDictionary<string, object?>>>> GetRoomReportAsync(int? locationId, int periodMonths, DateTime businessNow,
            DateTime? from = null, DateTime? to = null)
        {
            try
            {
                return await RunWithRangeAsync("dbo.WN_Ceo_RoomReport", p =>
                {
                    p.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                    p.Add("@PMonths", SqlDbType.Int).Value = periodMonths;
                    p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                }, from, to);
            }
            catch (CeoDatabaseUpdateMissingException ex) when (ex.Update == "CEO_01") // this procedure comes with CEO_02, not CEO_01
            {
                throw CeoDatabaseUpdateMissingException.RoomReportMissing(ex.InnerException);
            }
        }
    }
}
