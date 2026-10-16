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
        Task<List<List<IDictionary<string, object?>>>> GetDashboardAsync(int? locationId, int periodMonths, DateTime businessNow,
            CeoInvoiceStatusIds statuses, int expiringDays);

        /// <summary>Metric inquiries-type: narrowed to <paramref name="locationId"/> when the procedure knows how (QA_07).</summary>
        Task<List<List<IDictionary<string, object?>>>> GetBreakdownAsync(string metric, DateTime? monthStart, string? bucket,
            int? spaceTypeId, int? locationId, string? inquiryType, int? userId, DateTime businessNow, CeoInvoiceStatusIds statuses);

        Task<List<List<IDictionary<string, object?>>>> GetInvoiceRecordsAsync(DateTime? monthStart, int? locationId, bool noLocation,
            string? bucket, int? customerId, string? status, DateTime businessNow, CeoInvoiceStatusIds statuses, int top);

        Task<List<List<IDictionary<string, object?>>>> GetSpaceRecordsAsync(int? spaceTypeId, int? locationId, bool noLocation,
            bool? occupied, DateTime businessNow, int top);

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
        Task<List<List<IDictionary<string, object?>>>> GetRoomReportAsync(int? locationId, int periodMonths, DateTime businessNow);
    }

    /// <summary>
    /// CEO dashboard: all locations, read-only. Money, occupancy, sales and team (level 1), breakdowns (level 2) and
    /// record lists (level 3). Bad parameters give a failed ApiResponse (400).
    /// </summary>
    public interface ICeoDashboardService
    {
        Task<ApiResponse> GetDashboardAsync(string? period, int? locationId);
        Task<ApiResponse> GetBreakdownAsync(CeoBreakdownQuery query);
        Task<ApiResponse> GetRecordsAsync(string kind, CeoRecordsQuery query);

        /// <summary>
        /// Rooms: occupancy per room and the price each was sold at against its list (or shift) price. Before CEO_02 a
        /// successful response with available = false and a message.
        /// </summary>
        Task<ApiResponse> GetRoomReportAsync(string? period, int? locationId);
    }

    /// <summary>
    /// A CEO dashboard procedure is not on this database yet: the dashboard ones (script CEO_01), the full list of
    /// leases expiring (QA_07) or the room report (CEO_02). <see cref="Update"/> names the script; the message says it in
    /// plain words.
    /// </summary>
    public sealed class CeoDatabaseUpdateMissingException : Exception
    {
        public const string UserMessage = "The CEO dashboard needs the database update CEO_01.";
        public const string LeasesMessage = "The full list of leases expiring needs the database update QA_07.";
        public const string RoomsMessage = "Run CEO_02_room_report on the database to see the room report.";

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
    }
}
