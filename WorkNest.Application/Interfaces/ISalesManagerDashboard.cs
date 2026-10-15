using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Raw result sets of the Sales Manager dashboard stored procedures (dbo.WN_SalesMgr_*, scripts SM_01_sales_manager,
    /// SM_02_sales_manager_desks and SM_03_sales_manager_complaints). Locations are passed as CSV (null = all). Throws
    /// <see cref="SalesManagerDatabaseUpdateMissingException"/> when the procedures are not on the database yet (SM_01;
    /// the complaints ones: SM_03), or are still the ones without desks (SM_02).
    /// </summary>
    public interface ISalesManagerDashboardRepository
    {
        Task<List<List<IDictionary<string, object?>>>> GetDashboardAsync(string? locationIds, string? pickerLocationIds, int periodMonths,
            DateTime businessNow, int expiringDays);

        Task<List<List<IDictionary<string, object?>>>> GetBreakdownAsync(string metric, string? locationIds, DateTime? monthStart,
            string? inquiryType, int? userId, DateTime businessNow);

        Task<List<List<IDictionary<string, object?>>>> GetQuotationRecordsAsync(string? locationIds, DateTime? from, DateTime? to,
            int? userId, string? stage, int top);

        Task<List<List<IDictionary<string, object?>>>> GetBookingRecordsAsync(string? locationIds, bool noLocation, DateTime? from,
            DateTime? to, string? statusIds, bool runningOnly, DateTime businessNow, int top);

        Task<List<List<IDictionary<string, object?>>>> GetInquiryRecordsAsync(DateTime? from, DateTime? to, string? inquiryType,
            string? outcome, string? reason, DateTime businessNow, int top);

        Task<List<List<IDictionary<string, object?>>>> GetLeaseRecordsAsync(string? locationIds, bool noLocation, DateTime? from,
            DateTime? to, int expiringDays, DateTime businessNow, int top);

        /// <summary>
        /// Complaints section (dbo.WN_SalesMgr_Complaints, SM_03). <paramref name="includeNoBranch"/>: also the complaints
        /// without a branch (WhatsApp), i.e. no single location chosen.
        /// </summary>
        Task<List<List<IDictionary<string, object?>>>> GetComplaintsAsync(string? locationIds, bool includeNoBranch, int periodMonths,
            DateTime businessNow, int oldestTop);

        /// <summary>
        /// Complaint records (dbo.WN_SalesMgr_Records_Complaints, SM_03). <paramref name="branch"/>: null = any, "" = no branch;
        /// <paramref name="month"/>: created in that month.
        /// </summary>
        Task<List<List<IDictionary<string, object?>>>> GetComplaintRecordsAsync(string? locationIds, bool includeNoBranch, string? status,
            string? category, string? branch, DateTime? month, int periodMonths, DateTime businessNow, int top);
    }

    /// <summary>
    /// Sales Manager dashboard: read-only, limited to the caller's locations (<see cref="SalesManagerScope"/>, checked by
    /// the controller) except tour inquiries (no location: all). Funnel, team, inquiries and renewals (level 1),
    /// breakdowns (level 2) and record lists (level 3). Bad parameters give a failed ApiResponse (400).
    /// </summary>
    public interface ISalesManagerDashboardService
    {
        Task<ApiResponse> GetDashboardAsync(string? period, SalesManagerScope scope);
        Task<ApiResponse> GetBreakdownAsync(SalesManagerBreakdownQuery query, SalesManagerScope scope);
        Task<ApiResponse> GetRecordsAsync(string kind, SalesManagerRecordsQuery query, SalesManagerScope scope);

        /// <summary>Complaints section (complaints have no location: matched by branch name).</summary>
        Task<ApiResponse> GetComplaintsAsync(string? period, SalesManagerScope scope);

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

        /// <summary>The script the database needs (SM_01 or SM_02).</summary>
        public string Update { get; }

        /// <summary>What was missing, for the log.</summary>
        public string Detail { get; }

        /// <summary>A procedure answered without its desk column: it is still the SM_01 version, SM_02 has not run.</summary>
        public static SalesManagerDatabaseUpdateMissingException DesksMissing(string procedure, string column) =>
            new(DesksUserMessage, "SM_02", $"{procedure} returned no column {column}");

        /// <summary>A complaints procedure is missing: SM_03 has not run.</summary>
        public static SalesManagerDatabaseUpdateMissingException ComplaintsMissing(string procedure, string detail) =>
            new(ComplaintsUserMessage, "SM_03", $"{procedure}: {detail}");
    }

    /// <summary>The request asks for something outside the caller's locations (answered 403).</summary>
    public sealed class SalesManagerForbiddenException : Exception
    {
        public SalesManagerForbiddenException(string message) : base(message)
        {
        }
    }
}
