using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Raw result sets of the Sales Manager dashboard stored procedures (dbo.WN_SalesMgr_*, scripts SM_01_sales_manager,
    /// SM_02_sales_manager_desks, SM_03_sales_manager_complaints and BR_02_sm_inquiries_branch). Locations are passed as
    /// CSV (null = all). Throws <see cref="SalesManagerDatabaseUpdateMissingException"/> when the procedures are not on
    /// the database yet (SM_01; the complaints ones: SM_03), or are still the ones without desks (SM_02). Tour inquiries
    /// follow the locations once BR_02 has run; before that the procedures count every branch's inquiries (the result
    /// then has no ByBranch / InqByBranch column).
    /// </summary>
    public interface ISalesManagerDashboardRepository
    {
        /// <summary>
        /// <paramref name="inquiriesNoBranch"/> (BR_02): tour inquiries of <paramref name="locationIds"/> plus those without a
        /// branch (true: no single location chosen) or only theirs (false).
        /// </summary>
        /// <remarks>
        /// CS_01: <paramref name="from"/> / <paramref name="to"/> (both or neither, days included) = a custom range instead of
        /// the calendar period; sent only when given (before CS_01: <see cref="SalesManagerDatabaseUpdateMissingException"/>
        /// naming CS_01).
        /// </remarks>
        Task<List<List<IDictionary<string, object?>>>> GetDashboardAsync(string? locationIds, string? pickerLocationIds, bool inquiriesNoBranch,
            int periodMonths, DateTime businessNow, int expiringDays, DateTime? from = null, DateTime? to = null);

        /// <summary><paramref name="inquiriesNoBranch"/>: as <see cref="GetDashboardAsync"/> (metric inquiries-type).</summary>
        Task<List<List<IDictionary<string, object?>>>> GetBreakdownAsync(string metric, string? locationIds, bool inquiriesNoBranch,
            DateTime? monthStart, string? inquiryType, int? userId, DateTime businessNow);

        Task<List<List<IDictionary<string, object?>>>> GetQuotationRecordsAsync(string? locationIds, DateTime? from, DateTime? to,
            int? userId, string? stage, int top);

        Task<List<List<IDictionary<string, object?>>>> GetBookingRecordsAsync(string? locationIds, bool noLocation, DateTime? from,
            DateTime? to, string? statusIds, bool runningOnly, DateTime businessNow, int top);

        /// <summary>
        /// Tour inquiry records (BR_02): of <paramref name="locationIds"/> (null = all), plus those without a branch when
        /// <paramref name="includeNoBranch"/> ("0" with true = only those without a branch).
        /// </summary>
        Task<List<List<IDictionary<string, object?>>>> GetInquiryRecordsAsync(string? locationIds, bool includeNoBranch, DateTime? from,
            DateTime? to, string? inquiryType, string? outcome, string? reason, DateTime businessNow, int top);

        Task<List<List<IDictionary<string, object?>>>> GetLeaseRecordsAsync(string? locationIds, bool noLocation, DateTime? from,
            DateTime? to, int expiringDays, DateTime businessNow, int top);

        /// <summary>
        /// Complaints section (dbo.WN_SalesMgr_Complaints, SM_03). <paramref name="includeNoBranch"/>: also the complaints
        /// without a branch (WhatsApp), i.e. no single location chosen.
        /// </summary>
        Task<List<List<IDictionary<string, object?>>>> GetComplaintsAsync(string? locationIds, bool includeNoBranch, int periodMonths,
            DateTime businessNow, int oldestTop, DateTime? from = null, DateTime? to = null);

        /// <summary>
        /// Complaint records (dbo.WN_SalesMgr_Records_Complaints, SM_03). <paramref name="branch"/>: null = any, "" = no branch;
        /// <paramref name="month"/>: created in that month.
        /// </summary>
        /// <remarks>CS_01: <paramref name="from"/> / <paramref name="to"/> = the custom range of the *_period statuses.</remarks>
        Task<List<List<IDictionary<string, object?>>>> GetComplaintRecordsAsync(string? locationIds, bool includeNoBranch, string? status,
            string? category, string? branch, DateTime? month, int periodMonths, DateTime businessNow, int top,
            DateTime? from = null, DateTime? to = null);
    }

    /// <summary>
    /// Sales Manager dashboard: read-only, limited to the caller's locations (<see cref="SalesManagerScope"/>, checked by
    /// the controller); tour inquiries by their branch (BR_02), plus the ones without a branch when no single location is
    /// chosen. Funnel, team, inquiries and renewals (level 1), breakdowns (level 2) and record lists (level 3). Bad
    /// parameters give a failed ApiResponse (400).
    /// </summary>
    public interface ISalesManagerDashboardService
    {
        /// <summary>period = month | quarter | year | custom (custom: <paramref name="from"/> / <paramref name="to"/>, yyyy-MM-dd).</summary>
        Task<ApiResponse> GetDashboardAsync(string? period, SalesManagerScope scope, string? from = null, string? to = null);
        Task<ApiResponse> GetBreakdownAsync(SalesManagerBreakdownQuery query, SalesManagerScope scope);
        Task<ApiResponse> GetRecordsAsync(string kind, SalesManagerRecordsQuery query, SalesManagerScope scope);

        /// <summary>Complaints section (complaints have no location: matched by branch name).</summary>
        Task<ApiResponse> GetComplaintsAsync(string? period, SalesManagerScope scope, string? from = null, string? to = null);

        /// <summary>Complaint records behind a complaints card, status, category, branch or month.</summary>
        Task<ApiResponse> GetComplaintRecordsAsync(SalesManagerComplaintRecordsQuery query, SalesManagerScope scope);
    }

    /// <summary>
    /// The database is not ready for this API: the Sales Manager dashboard procedures are missing (script SM_01 not run),
    /// or they are still the SM_01 ones, without desks (script SM_02 not run), or the complaints procedures are missing
    /// (script SM_03 not run). <see cref="Exception.Message"/> is the text for the caller (answered 503).
    /// </summary>
    public sealed class SalesManagerDatabaseUpdateMissingException : Exception
    {
        public const string UserMessage = "The Sales Manager dashboard needs the database update SM_01.";
        public const string DesksUserMessage = "The Sales Manager dashboard needs the database update SM_02.";
        public const string ComplaintsUserMessage = "The complaints section needs the database update SM_03.";
        public const string CustomRangeUserMessage = DashboardDatabaseUpdate.CustomRangeMessage;

        /// <summary>The procedures are missing: SM_01.</summary>
        public SalesManagerDatabaseUpdateMissingException(Exception inner) : base(UserMessage, inner)
        {
            Update = "SM_01";
            Detail = inner.Message;
        }

        private SalesManagerDatabaseUpdateMissingException(string message, string update, string detail) : base(message)
        {
            Update = update;
            Detail = detail;
        }

        /// <summary>The script the database needs (SM_01, SM_02, SM_03 or CS_01).</summary>
        public string Update { get; }

        /// <summary>What was missing, for the log.</summary>
        public string Detail { get; }

        /// <summary>A procedure answered without its desk column: it is still the SM_01 version, SM_02 has not run.</summary>
        public static SalesManagerDatabaseUpdateMissingException DesksMissing(string procedure, string column) =>
            new(DesksUserMessage, "SM_02", $"{procedure} returned no column {column}");

        /// <summary>A complaints procedure is missing: SM_03 has not run.</summary>
        public static SalesManagerDatabaseUpdateMissingException ComplaintsMissing(string procedure, string detail) =>
            new(ComplaintsUserMessage, "SM_03", $"{procedure}: {detail}");

        /// <summary>A procedure does not know @From / @To (SQL error 8144 / 8145): CS_01 has not run.</summary>
        public static SalesManagerDatabaseUpdateMissingException CustomRangeMissing(string procedure, string detail) =>
            new(CustomRangeUserMessage, DashboardDatabaseUpdate.CustomRange, $"{procedure}: {detail}");
    }

    /// <summary>The request asks for something outside the caller's locations (answered 403).</summary>
    public sealed class SalesManagerForbiddenException : Exception
    {
        public SalesManagerForbiddenException(string message) : base(message)
        {
        }
    }
}
