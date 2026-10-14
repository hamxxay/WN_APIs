using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// CEO dashboard data: calls the read-only stored procedures dbo.WN_Ceo_* (script CEO_01_dashboard_procedures).
    /// Until that script has run the procedures are missing (SQL error 2812) and every call throws
    /// <see cref="CeoDatabaseUpdateMissingException"/>, which the API answers with 503.
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

        public Task<List<List<IDictionary<string, object?>>>> GetDashboardAsync(int? locationId, int periodMonths, DateTime businessNow,
            CeoInvoiceStatusIds statuses, int expiringDays) =>
            RunAsync("dbo.WN_Ceo_Dashboard", p =>
            {
                p.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                p.Add("@PMonths", SqlDbType.Int).Value = periodMonths;
                p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                AddStatuses(p, statuses);
                p.Add("@ExpiringDays", SqlDbType.Int).Value = expiringDays;
            });

        public Task<List<List<IDictionary<string, object?>>>> GetBreakdownAsync(string metric, DateTime? monthStart, string? bucket,
            int? spaceTypeId, int? locationId, string? inquiryType, int? userId, DateTime businessNow, CeoInvoiceStatusIds statuses) =>
            RunAsync("dbo.WN_Ceo_Breakdown", p =>
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
            });

        public Task<List<List<IDictionary<string, object?>>>> GetInvoiceRecordsAsync(DateTime? monthStart, int? locationId, bool noLocation,
            string? bucket, int? customerId, string? status, DateTime businessNow, CeoInvoiceStatusIds statuses, int top) =>
            RunAsync("dbo.WN_Ceo_Records_Invoices", p =>
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
            });

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

        public Task<List<List<IDictionary<string, object?>>>> GetInquiryRecordsAsync(DateTime? from, DateTime? to, string? inquiryType, int top) =>
            RunAsync("dbo.WN_Ceo_Records_Inquiries", p =>
            {
                p.Add("@From", SqlDbType.DateTime2).Value = Db(from);
                p.Add("@To", SqlDbType.DateTime2).Value = Db(to);
                p.Add("@InquiryType", SqlDbType.NVarChar, 20).Value = Db(inquiryType);
                p.Add("@Top", SqlDbType.Int).Value = top;
            });

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
    }
}
