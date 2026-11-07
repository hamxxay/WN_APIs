using System.Data;
using System.Globalization;
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

        /// <summary>
        /// Normalizes <paramref name="locationId"/> and <paramref name="locationIds"/>: when a single location is targeted
        /// (either explicitly via <paramref name="locationId"/> or a 1-element <paramref name="locationIds"/>), uses
        /// <c>@LocationId</c> and clears <paramref name="locationIds"/> so procedures without <c>@LocationIds</c> work
        /// without extra parameters.
        /// </summary>
        private static void NormalizeLocationScope(ref int? locationId, ref string? locationIds, out List<int> multiLocations)
        {
            var ids = (locationIds ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (locationId is > 0)
            {
                locationIds = null;
                multiLocations = new List<int>();
                return;
            }
            if (ids.Count == 1)
            {
                locationId = ids[0];
                locationIds = null;
                multiLocations = new List<int>();
                return;
            }
            if (ids.Count > 1)
            {
                locationIds = string.Join(",", ids);
                multiLocations = ids;
                return;
            }
            locationIds = null;
            multiLocations = new List<int>();
        }

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

        public async Task<List<List<IDictionary<string, object?>>>> GetDashboardAsync(int? locationId, int periodMonths, DateTime businessNow,
            CeoInvoiceStatusIds statuses, int expiringDays, DateTime? from = null, DateTime? to = null, string? locationIds = null)
        {
            NormalizeLocationScope(ref locationId, ref locationIds, out var multiLocations);
            var idsCsv = locationIds;
            var loc = locationId;

            Task<List<List<IDictionary<string, object?>>>> CallOnce(int? lid, string? csv) =>
                RunWithInquiryLocationAsync("dbo.WN_Ceo_Dashboard", p =>
                {
                    p.Add("@LocationId", SqlDbType.Int).Value = Db(lid);
                    p.Add("@PMonths", SqlDbType.Int).Value = periodMonths;
                    p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                    AddStatuses(p, statuses);
                    p.Add("@ExpiringDays", SqlDbType.Int).Value = expiringDays;
                    if (!string.IsNullOrWhiteSpace(csv))
                        p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = csv;
                }, p => p.Add("@InquiriesByLocation", SqlDbType.Bit).Value = true, from, to);

            if (multiLocations.Count <= 1)
                return await CallOnce(loc, null);

            try
            {
                return await CallOnce(loc, idsCsv);
            }
            catch (Exception ex) when (IsMissingParameterException(ex))
            {
                // Procedure does not have @LocationIds yet: query each assigned location via @LocationId and merge.
                var perLocation = new List<List<List<IDictionary<string, object?>>>>(multiLocations.Count);
                foreach (var lid in multiLocations)
                    perLocation.Add(await CallOnce(lid, null));
                return MergeDashboardSets(perLocation, multiLocations);
            }
        }

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

        public async Task<List<List<IDictionary<string, object?>>>> GetSpaceRecordsAsync(int? spaceTypeId, int? locationId, bool noLocation,
            bool? occupied, DateTime businessNow, int top, string? categoryCode = null, bool roomsOnly = false)
        {
            try
            {
                return await RunAsync("dbo.WN_Ceo_Records_Spaces", p =>
                {
                    p.Add("@SpaceTypeId", SqlDbType.Int).Value = Db(spaceTypeId);
                    p.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                    p.Add("@NoLocation", SqlDbType.Bit).Value = noLocation;
                    p.Add("@Occupied", SqlDbType.Bit).Value = Db(occupied);
                    p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                    p.Add("@Top", SqlDbType.Int).Value = top;
                    // CP_02: only when asked for, so the lists keep working with the procedure from before CP_02.
                    if (categoryCode != null) p.Add("@CategoryCode", SqlDbType.NVarChar, 30).Value = categoryCode;
                    if (roomsOnly) p.Add("@RoomsOnly", SqlDbType.Bit).Value = true;
                });
            }
            catch (SqlException ex) when (ex.Number is 8144 or 8145 && (categoryCode != null || roomsOnly)) // CP_02 not run yet
            {
                throw CeoDatabaseUpdateMissingException.OccupancyCategoryMissing(ex);
            }
        }

        public async Task<List<List<IDictionary<string, object?>>>> GetOccupancyCategoryBreakdownAsync(string categoryCode, int? locationId,
            DateTime businessNow)
        {
            try
            {
                return await RunAsync("dbo.WN_Ceo_Breakdown", p =>
                {
                    p.Add("@Metric", SqlDbType.NVarChar, 30).Value = "occupancy-category";
                    p.Add("@CategoryCode", SqlDbType.NVarChar, 30).Value = categoryCode;
                    p.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                    p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                });
            }
            catch (SqlException ex) when (ex.Number is 8144 or 8145) // CP_02 not run yet: no @CategoryCode
            {
                throw CeoDatabaseUpdateMissingException.OccupancyCategoryMissing(ex);
            }
        }

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

        public async Task<string?> GetBookingStatusIdsAsync(IReadOnlyCollection<string> codes)
        {
            var list = codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count == 0) return null;
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                var names = list.Select((_, i) => $"@c{i}").ToList();
                await using var cmd = new SqlCommand(
                    $"SELECT Id FROM dbo.WN_BookingStatuses WHERE Code IN ({string.Join(", ", names)}) ORDER BY Id", c) { CommandTimeout = 30 };
                for (var i = 0; i < list.Count; i++) cmd.Parameters.Add(names[i], SqlDbType.NVarChar, 50).Value = list[i];
                var ids = new List<int>();
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) ids.Add(Convert.ToInt32(r.GetValue(0)));
                return ids.Count == 0 ? null : Csv(ids);
            }
            catch (SqlException ex) when (ex.Number is 207 or 208) // no WN_BookingStatuses (or no Code column) on this database
            {
                return null;
            }
        }

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
            DateTime? from = null, DateTime? to = null, string? locationIds = null)
        {
            NormalizeLocationScope(ref locationId, ref locationIds, out var multiLocations);
            var idsCsv = locationIds;
            var loc = locationId;

            Task<List<List<IDictionary<string, object?>>>> CallOnce(int? lid, string? csv) =>
                RunWithRangeAsync("dbo.WN_Ceo_RoomReport", p =>
                {
                    p.Add("@LocationId", SqlDbType.Int).Value = Db(lid);
                    p.Add("@PMonths", SqlDbType.Int).Value = periodMonths;
                    p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                    if (!string.IsNullOrWhiteSpace(csv))
                        p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = csv;
                }, from, to);

            try
            {
                if (multiLocations.Count <= 1)
                    return await CallOnce(loc, null);

                try
                {
                    return await CallOnce(loc, idsCsv);
                }
                catch (Exception ex) when (IsMissingParameterException(ex))
                {
                    // Procedure does not have @LocationIds yet: fetch all and filter rooms/sales to multiLocations.
                    var all = await CallOnce(null, null);
                    return FilterRoomReportSets(all, multiLocations);
                }
            }
            catch (CeoDatabaseUpdateMissingException ex) when (ex.Update == "CEO_01") // this procedure comes with CEO_02, not CEO_01
            {
                throw CeoDatabaseUpdateMissingException.RoomReportMissing(ex.InnerException);
            }
        }

        // CP_02: money received (fee receipts + paid payments by their day). Both procedures come with CP_02; before it
        // (SQL error 2812 -> CeoDatabaseUpdateMissingException) the answer is null, so the dashboard still loads.

        public async Task<List<List<IDictionary<string, object?>>>?> GetReceivedAsync(int? locationId, int periodMonths, DateTime businessNow,
            DateTime? from = null, DateTime? to = null, string? locationIds = null)
        {
            NormalizeLocationScope(ref locationId, ref locationIds, out var multiLocations);
            var idsCsv = locationIds;
            var loc = locationId;

            Task<List<List<IDictionary<string, object?>>>> CallOnce(int? lid, string? csv) =>
                RunAsync("dbo.WN_Ceo_Received", p =>
                {
                    p.Add("@LocationId", SqlDbType.Int).Value = Db(lid);
                    p.Add("@PMonths", SqlDbType.Int).Value = periodMonths;
                    p.Add("@BizNow", SqlDbType.DateTime2).Value = businessNow;
                    p.Add("@From", SqlDbType.Date).Value = Db(from?.Date);
                    p.Add("@To", SqlDbType.Date).Value = Db(to?.Date);
                    if (!string.IsNullOrWhiteSpace(csv))
                        p.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = csv;
                });

            try
            {
                if (multiLocations.Count <= 1)
                    return await CallOnce(loc, null);

                try
                {
                    return await CallOnce(loc, idsCsv);
                }
                catch (SqlException ex) when (ex.Number is 8144 or 8145)
                {
                    var perLocation = new List<List<List<IDictionary<string, object?>>>>(multiLocations.Count);
                    foreach (var lid in multiLocations)
                        perLocation.Add(await CallOnce(lid, null));
                    return MergeReceivedSets(perLocation);
                }
            }
            catch (CeoDatabaseUpdateMissingException)
            {
                return null;
            }
            catch (SqlException ex) when (ex.Number == 208) // a table it reads (WN_FeeReceipts) is not on this database
            {
                return null;
            }
        }

        public async Task<List<List<IDictionary<string, object?>>>?> GetReceiptRecordsAsync(DateTime? from, DateTime? to, int? locationId,
            bool noLocation, int top)
        {
            try
            {
                return await RunAsync("dbo.WN_Ceo_Records_Receipts", p =>
                {
                    p.Add("@From", SqlDbType.Date).Value = Db(from?.Date);
                    p.Add("@To", SqlDbType.Date).Value = Db(to?.Date);
                    p.Add("@LocationId", SqlDbType.Int).Value = Db(locationId);
                    p.Add("@NoLocation", SqlDbType.Bit).Value = noLocation;
                    p.Add("@Top", SqlDbType.Int).Value = top;
                });
            }
            catch (CeoDatabaseUpdateMissingException)
            {
                return null;
            }
            catch (SqlException ex) when (ex.Number == 208) // a table it reads (WN_FeeReceipts) is not on this database
            {
                return null;
            }
        }

        private static bool IsMissingParameterException(Exception ex) =>
            ex is SqlException { Number: 8144 or 8145 }
            || ex is CeoDatabaseUpdateMissingException { Update: "CS_01", InnerException: SqlException { Number: 8144 or 8145 } };

        private static List<List<IDictionary<string, object?>>> FilterRoomReportSets(
            List<List<IDictionary<string, object?>>> sets, IReadOnlyCollection<int> allowedLocations)
        {
            if (sets.Count == 0) return sets;
            var allowed = new HashSet<int>(allowedLocations);
            var keptSpaceIds = new HashSet<int>();
            var rooms = new List<IDictionary<string, object?>>();
            foreach (var r in sets[0])
            {
                if (r.TryGetValue("LocationId", out var lv) && lv != null && allowed.Contains(Convert.ToInt32(lv, CultureInfo.InvariantCulture)))
                {
                    rooms.Add(r);
                    if (r.TryGetValue("SpaceId", out var sv) && sv != null)
                        keptSpaceIds.Add(Convert.ToInt32(sv, CultureInfo.InvariantCulture));
                }
            }
            var sales = new List<IDictionary<string, object?>>();
            if (sets.Count > 1)
            {
                foreach (var r in sets[1])
                {
                    if (r.TryGetValue("SpaceId", out var sv) && sv != null && keptSpaceIds.Contains(Convert.ToInt32(sv, CultureInfo.InvariantCulture)))
                        sales.Add(r);
                }
            }
            var result = new List<List<IDictionary<string, object?>>> { rooms, sales };
            for (var i = 2; i < sets.Count; i++) result.Add(sets[i]);
            return result;
        }

        private static List<List<IDictionary<string, object?>>> MergeReceivedSets(
            IReadOnlyList<List<List<IDictionary<string, object?>>>> perLocation)
        {
            if (perLocation.Count == 0) return new();
            if (perLocation.Count == 1) return perLocation[0];

            var totals = SumSingleRowSet(perLocation.Select(s => s.Count > 0 ? s[0] : new()));
            var months = SumGroupedRows(perLocation.Select(s => s.Count > 1 ? s[1] : new()), "Month");
            return new List<List<IDictionary<string, object?>>> { totals, months };
        }

        private static List<List<IDictionary<string, object?>>> MergeDashboardSets(
            IReadOnlyList<List<List<IDictionary<string, object?>>>> perLocation, IReadOnlyCollection<int> allowedLocations)
        {
            if (perLocation.Count == 0) return new();
            if (perLocation.Count == 1) return perLocation[0];

            var allowed = new HashSet<int>(allowedLocations);
            var maxSets = perLocation.Max(s => s.Count);
            var merged = new List<List<IDictionary<string, object?>>>(maxSets);

            List<IDictionary<string, object?>> SetAt(List<List<IDictionary<string, object?>>> s, int idx) =>
                idx < s.Count ? s[idx] : new();

            for (var idx = 0; idx < maxSets; idx++)
            {
                var slices = perLocation.Select(s => SetAt(s, idx)).ToList();
                switch (idx)
                {
                    case 0: // Locations
                        merged.Add(DistinctByKey(slices, "LocationId")
                            .Where(r => r.TryGetValue("LocationId", out var v) && v != null && allowed.Contains(Convert.ToInt32(v, CultureInfo.InvariantCulture)))
                            .ToList());
                        break;
                    case 1: // Money summary row
                    case 5: // Occupancy summary row
                    case 9: // Sales summary row
                    case 13: // Lease totals row
                        merged.Add(SumSingleRowSet(slices));
                        break;
                    case 2: // Months
                        merged.Add(SumGroupedRows(slices, "Month"));
                        break;
                    case 3: // Aging buckets
                        merged.Add(SumGroupedRows(slices, "BucketKey"));
                        break;
                    case 4: // TopCustomers
                        merged.Add(SumGroupedRows(slices, "CustomerId", "Name")
                            .OrderByDescending(r => r.TryGetValue("Amount", out var a) && a != null ? Convert.ToDecimal(a, CultureInfo.InvariantCulture) : 0m)
                            .Take(5)
                            .ToList());
                        break;
                    case 6: // Occupancy by category / space type
                        merged.Add(SumGroupedRows(slices, "CategoryCode", "SpaceTypeId", "Name"));
                        break;
                    case 7: // Occupancy by location
                    case 11: // Team per location
                        merged.Add(DistinctByKey(slices, "LocationId")
                            .Where(r => r.TryGetValue("LocationId", out var v) && v != null && allowed.Contains(Convert.ToInt32(v, CultureInfo.InvariantCulture)))
                            .ToList());
                        break;
                    case 8: // LeasesExpiring
                        merged.Add(DistinctByKey(slices, "BookingId")
                            .OrderBy(r => r.TryGetValue("EndOn", out var d) && d is DateTime dt ? dt : DateTime.MaxValue)
                            .Take(50)
                            .ToList());
                        break;
                    case 10: // Inquiries by type
                        merged.Add(SumGroupedRows(slices, "InquiryType"));
                        break;
                    case 12: // Team people
                        merged.Add(SumGroupedRows(slices, "UserId", "RoleId", "Name", "Locations"));
                        break;
                    default:
                        merged.Add(slices.FirstOrDefault() ?? new());
                        break;
                }
            }

            return merged;
        }

        private static List<IDictionary<string, object?>> DistinctByKey(
            IEnumerable<List<IDictionary<string, object?>>> slices, string keyColumn)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<IDictionary<string, object?>>();
            foreach (var rows in slices)
            {
                foreach (var r in rows)
                {
                    var key = r.TryGetValue(keyColumn, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";
                    if (seen.Add(key))
                        list.Add(new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase));
                }
            }
            return list;
        }

        private static List<IDictionary<string, object?>> SumSingleRowSet(
            IEnumerable<List<IDictionary<string, object?>>> slices)
        {
            IDictionary<string, object?>? acc = null;
            foreach (var rows in slices)
            {
                var row = rows.FirstOrDefault();
                if (row == null) continue;
                if (acc == null)
                {
                    acc = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    AccumulateRow(acc, row, Array.Empty<string>());
                }
            }
            return acc == null ? new() : new List<IDictionary<string, object?>> { acc };
        }

        private static List<IDictionary<string, object?>> SumGroupedRows(
            IEnumerable<List<IDictionary<string, object?>>> slices, params string[] keyColumns)
        {
            var keySet = new HashSet<string>(keyColumns, StringComparer.OrdinalIgnoreCase);
            var map = new Dictionary<string, IDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            foreach (var rows in slices)
            {
                foreach (var row in rows)
                {
                    var key = string.Join("|", keyColumns.Select(k =>
                        row.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : ""));
                    if (!map.TryGetValue(key, out var acc))
                    {
                        acc = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase);
                        map[key] = acc;
                        order.Add(key);
                    }
                    else
                    {
                        AccumulateRow(acc, row, keySet);
                    }
                }
            }

            return order.Select(k => map[k]).ToList();
        }

        private static void AccumulateRow(IDictionary<string, object?> acc, IDictionary<string, object?> row, ICollection<string> skipColumns)
        {
            foreach (var (col, val) in row)
            {
                if (skipColumns.Contains(col) || col.EndsWith("Id", StringComparison.OrdinalIgnoreCase) || col.Equals("SortOrder", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (val == null) continue;
                if (!acc.TryGetValue(col, out var cur) || cur == null)
                {
                    acc[col] = val;
                    continue;
                }
                if (cur is decimal or double or float || val is decimal or double or float)
                {
                    acc[col] = Convert.ToDecimal(cur, CultureInfo.InvariantCulture) + Convert.ToDecimal(val, CultureInfo.InvariantCulture);
                }
                else if (cur is int or long or short or byte || val is int or long or short or byte)
                {
                    acc[col] = Convert.ToInt32(cur, CultureInfo.InvariantCulture) + Convert.ToInt32(val, CultureInfo.InvariantCulture);
                }
            }
        }
    }
}
