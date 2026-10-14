using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Raw result sets of the Sales Manager dashboard stored procedures (dbo.WN_SalesMgr_*, script SM_01_sales_manager).
    /// Locations are passed as CSV (null = all). Throws <see cref="SalesManagerDatabaseUpdateMissingException"/> when the
    /// procedures are not on the database yet.
    /// </summary>
    public interface ISalesManagerDashboardRepository
    {
        Task<List<List<IDictionary<string, object?>>>> GetDashboardAsync(string? locationIds, string? pickerLocationIds, int periodMonths,
            DateTime businessNow, string voidStatusIds, int expiringDays);

        Task<List<List<IDictionary<string, object?>>>> GetBreakdownAsync(string metric, string? locationIds, DateTime? monthStart,
            string? inquiryType, int? userId, DateTime businessNow, string voidStatusIds);

        Task<List<List<IDictionary<string, object?>>>> GetQuotationRecordsAsync(string? locationIds, DateTime? from, DateTime? to,
            int? userId, string? stage, int top);

        Task<List<List<IDictionary<string, object?>>>> GetBookingRecordsAsync(string? locationIds, bool noLocation, DateTime? from,
            DateTime? to, string? statusIds, bool runningOnly, DateTime businessNow, int top);

        Task<List<List<IDictionary<string, object?>>>> GetInquiryRecordsAsync(DateTime? from, DateTime? to, string? inquiryType,
            string? outcome, string? reason, DateTime businessNow, int top);

        Task<List<List<IDictionary<string, object?>>>> GetLeaseRecordsAsync(string? locationIds, bool noLocation, DateTime? from,
            DateTime? to, int expiringDays, DateTime businessNow, int top);
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
    }

    /// <summary>The Sales Manager dashboard procedures (script SM_01) are not on this database yet.</summary>
    public sealed class SalesManagerDatabaseUpdateMissingException : Exception
    {
        public const string UserMessage = "The Sales Manager dashboard needs the database update SM_01.";

        public SalesManagerDatabaseUpdateMissingException(Exception inner) : base(UserMessage, inner)
        {
        }
    }

    /// <summary>The request asks for something outside the caller's locations (answered 403).</summary>
    public sealed class SalesManagerForbiddenException : Exception
    {
        public SalesManagerForbiddenException(string message) : base(message)
        {
        }
    }
}
