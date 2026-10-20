using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Invoice StatusIds by meaning (OrderStatus IDs + legacy values), as the overview uses them.</summary>
    public sealed record CeoInvoiceStatusIds(IReadOnlyCollection<int> Open, IReadOnlyCollection<int> Paid, IReadOnlyCollection<int> Void);

    /// <summary>
    /// Raw result sets of the CEO dashboard stored procedures (dbo.WN_Ceo_*, script CEO_01_dashboard_procedures, changed
    /// by QA_07_dashboards). Throws <see cref="CeoDatabaseUpdateMissingException"/> when the procedures are not on the
    /// database yet. Tour inquiries follow the location filter once QA_07 has run (by their branch,
    /// WN_Contacts.LocationId); before that the procedures count every location's inquiries (the results then have no
    /// InqByLocation / ByLocation column).
    /// </summary>
    public interface ICeoDashboardRepository
    {
        /// <summary>Tour inquiries narrowed to <paramref name="locationId"/> when the procedure knows how (QA_07).</summary>
        /// <remarks>
        /// CS_01: <paramref name="from"/> / <paramref name="to"/> (both or neither, days included) = a custom range instead of
        /// the calendar period; sent to the procedure only when given (before CS_01: <see cref="CeoDatabaseUpdateMissingException"/>
        /// naming CS_01).
        /// </remarks>
        Task<List<List<IDictionary<string, object?>>>> GetDashboardAsync(int? locationId, int periodMonths, DateTime businessNow,
            CeoInvoiceStatusIds statuses, int expiringDays, DateTime? from = null, DateTime? to = null);

        /// <summary>Metric inquiries-type: narrowed to <paramref name="locationId"/> when the procedure knows how (QA_07).</summary>
        /// <remarks>CS_01, metric revenue: <paramref name="from"/> / <paramref name="to"/> (days included) instead of a month.</remarks>
        Task<List<List<IDictionary<string, object?>>>> GetBreakdownAsync(string metric, DateTime? monthStart, string? bucket,
            int? spaceTypeId, int? locationId, string? inquiryType, int? userId, DateTime businessNow, CeoInvoiceStatusIds statuses,
            DateTime? from = null, DateTime? to = null);

        /// <remarks>
        /// CS_01: <paramref name="from"/> / <paramref name="to"/> (days included) = billing period starting in that range (CP_01;
        /// before it: issued in it). <paramref name="status"/>: open | overdue | paid | advance (CP_01: billed in advance).
        /// </remarks>
        Task<List<List<IDictionary<string, object?>>>> GetInvoiceRecordsAsync(DateTime? monthStart, int? locationId, bool noLocation,
            string? bucket, int? customerId, string? status, DateTime businessNow, CeoInvoiceStatusIds statuses, int top,
            DateTime? from = null, DateTime? to = null);

        /// <remarks>
        /// CP_02: <paramref name="categoryCode"/> (PrivateOffice | SharedSpace) / <paramref name="roomsOnly"/> are sent only
        /// when asked for; before CP_02 they give <see cref="CeoDatabaseUpdateMissingException"/> naming CP_02.
        /// </remarks>
        Task<List<List<IDictionary<string, object?>>>> GetSpaceRecordsAsync(int? spaceTypeId, int? locationId, bool noLocation,
            bool? occupied, DateTime businessNow, int top, string? categoryCode = null, bool roomsOnly = false);

        /// <summary>
        /// CP_02: dbo.WN_Ceo_Breakdown metric occupancy-category (@CategoryCode = PrivateOffice | SharedSpace): occupied /
        /// total rooms of the category per location, right now. Before CP_02: <see cref="CeoDatabaseUpdateMissingException"/>
        /// naming CP_02.
        /// </summary>
        Task<List<List<IDictionary<string, object?>>>> GetOccupancyCategoryBreakdownAsync(string categoryCode, int? locationId,
            DateTime businessNow);

        Task<List<List<IDictionary<string, object?>>>> GetBookingRecordsAsync(DateTime? from, DateTime? to, int? locationId, bool noLocation,
            string? statusIds, bool runningOnly, DateTime businessNow, int top);

        /// <summary>
        /// Tour inquiries of one branch (<paramref name="locationId"/>, QA_07; null = every inquiry). Before QA_07 every
        /// inquiry (the first result then has no ByLocation column).
        /// </summary>
        Task<List<List<IDictionary<string, object?>>>> GetInquiryRecordsAsync(DateTime? from, DateTime? to, string? inquiryType,
            int? locationId, int top);

        Task<List<List<IDictionary<string, object?>>>> GetQuotationRecordsAsync(DateTime? from, DateTime? to, int? userId, string? stage,
            int? locationId, int top);

        /// <summary>
        /// CP_02: the new bookings created in the window (as the Sales funnel's Bookings stage) at their contract value, with
        /// their seller; <paramref name="sellerId"/> = only those this user sold (dbo.WN_Ceo_Records_Sales). Before CP_02:
        /// <see cref="CeoDatabaseUpdateMissingException"/> naming CP_02.
        /// </summary>
        Task<List<List<IDictionary<string, object?>>>> GetSalesRecordsAsync(DateTime? from, DateTime? to, int? locationId, int? sellerId, int top);

        /// <summary>
        /// Confirmed leases ending in the window (default: the next <paramref name="expiringDays"/> days), soonest first
        /// (dbo.WN_Ceo_Records_Leases, QA_07; before it: <see cref="CeoDatabaseUpdateMissingException"/> naming QA_07).
        /// </summary>
        Task<List<List<IDictionary<string, object?>>>> GetLeaseRecordsAsync(int? locationId, bool noLocation, DateTime? from, DateTime? to,
            int expiringDays, DateTime businessNow, int top);

        /// <summary>
        /// Room report (dbo.WN_Ceo_RoomReport, CEO_02): [0] rooms sold monthly with their list price per seat and
        /// occupancy, [1] their confirmed bookings running in the period. Before CEO_02:
        /// <see cref="CeoDatabaseUpdateMissingException"/> naming CEO_02.
        /// </summary>
        /// <remarks>CS_01: <paramref name="from"/> / <paramref name="to"/> (days included) = a custom range instead of the period.</remarks>
        Task<List<List<IDictionary<string, object?>>>> GetRoomReportAsync(int? locationId, int periodMonths, DateTime businessNow,
            DateTime? from = null, DateTime? to = null);
    }

    /// <summary>
    /// CEO dashboard: all locations, read-only. Money, occupancy, sales and team (level 1), breakdowns (level 2) and
    /// record lists (level 3). Bad parameters give a failed ApiResponse (400).
    /// </summary>
    public interface ICeoDashboardService
    {
        /// <summary>period = month | quarter | year | custom (custom: <paramref name="from"/> / <paramref name="to"/>, yyyy-MM-dd).</summary>
        Task<ApiResponse> GetDashboardAsync(string? period, int? locationId, string? from = null, string? to = null);
        Task<ApiResponse> GetBreakdownAsync(CeoBreakdownQuery query);
        Task<ApiResponse> GetRecordsAsync(string kind, CeoRecordsQuery query);

        /// <summary>
        /// Rooms: occupancy per room and the price each was sold at against its list (or shift) price. Before CEO_02 a
        /// successful response with available = false and a message.
        /// </summary>
        Task<ApiResponse> GetRoomReportAsync(string? period, int? locationId, string? from = null, string? to = null);
    }

    /// <summary>
    /// A CEO dashboard procedure is not on this database yet: the dashboard ones (script CEO_01), the full list of
    /// leases expiring (QA_07), the room report (CEO_02), or the procedures do not take a custom date range yet (CS_01). <see cref="Update"/> names the script; the message says it in
    /// plain words.
    /// </summary>
    public sealed class CeoDatabaseUpdateMissingException : Exception
    {
        public const string UserMessage = "The CEO dashboard needs the database update CEO_01.";
        public const string LeasesMessage = "The full list of leases expiring needs the database update QA_07.";
        public const string RoomsMessage = "Run CEO_02_room_report on the database to see the room report.";
        public const string CustomRangeMessage = DashboardDatabaseUpdate.CustomRangeMessage;

        /// <summary>The database update (script) that is missing, e.g. CEO_01 or QA_07.</summary>
        public string Update { get; }

        public CeoDatabaseUpdateMissingException(Exception inner) : this(inner, "CEO_01", UserMessage)
        {
        }

        private CeoDatabaseUpdateMissingException(Exception? inner, string update, string message) : base(message, inner)
        {
            Update = update;
        }

        /// <summary>dbo.WN_Ceo_Records_Leases is missing: QA_07 has not run.</summary>
        public static CeoDatabaseUpdateMissingException LeasesMissing(Exception? inner) => new(inner, "QA_07", LeasesMessage);

        /// <summary>dbo.WN_Ceo_RoomReport is missing: CEO_02 has not run.</summary>
        public static CeoDatabaseUpdateMissingException RoomReportMissing(Exception? inner) => new(inner, "CEO_02", RoomsMessage);

        public const string OccupancyMessage = "Occupancy by room category needs the database update CP_02.";

        /// <summary>A procedure does not know the CP_02 room category parameters (SQL error 8144 / 8145): CP_02 has not run.</summary>
        public static CeoDatabaseUpdateMissingException OccupancyCategoryMissing(Exception? inner) => new(inner, "CP_02", OccupancyMessage);

        /// <summary>dbo.WN_Ceo_Records_Sales is missing: CP_02 has not run.</summary>
        public static CeoDatabaseUpdateMissingException SalesRecordsMissing(Exception? inner) =>
            new(inner, "CP_02", "The sales list needs the database update CP_02.");

        /// <summary>A procedure does not know @From / @To (SQL error 8144 / 8145): CS_01 has not run.</summary>
        public static CeoDatabaseUpdateMissingException CustomRangeMissing(Exception? inner) =>
            new(inner, DashboardDatabaseUpdate.CustomRange, CustomRangeMessage);
    }

    /// <summary>The database update that lets the CEO and Sales Manager dashboards take a custom date range.</summary>
    public static class DashboardDatabaseUpdate
    {
        /// <summary>Script CS_01_ceo_sales_dates (TEST / LIVE).</summary>
        public const string CustomRange = "CS_01";

        /// <remarks>No ".sql" in it: the CMS and the app hide any message that mentions SQL.</remarks>
        public const string CustomRangeMessage =
            "Database update needed: run the script CS_01_ceo_sales_dates_TEST (CS_01_ceo_sales_dates_LIVE on the live database) to use a custom date range.";
    }
}
