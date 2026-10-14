using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Invoice StatusIds by meaning (OrderStatus IDs + legacy values), as the overview uses them.</summary>
    public sealed record CeoInvoiceStatusIds(IReadOnlyCollection<int> Open, IReadOnlyCollection<int> Paid, IReadOnlyCollection<int> Void);

    /// <summary>
    /// Raw result sets of the CEO dashboard stored procedures (dbo.WN_Ceo_*, script CEO_01_dashboard_procedures).
    /// Throws <see cref="CeoDatabaseUpdateMissingException"/> when the procedures are not on the database yet.
    /// </summary>
    public interface ICeoDashboardRepository
    {
        Task<List<List<IDictionary<string, object?>>>> GetDashboardAsync(int? locationId, int periodMonths, DateTime businessNow,
            CeoInvoiceStatusIds statuses, int expiringDays);

        Task<List<List<IDictionary<string, object?>>>> GetBreakdownAsync(string metric, DateTime? monthStart, string? bucket,
            int? spaceTypeId, int? locationId, string? inquiryType, int? userId, DateTime businessNow, CeoInvoiceStatusIds statuses);

        Task<List<List<IDictionary<string, object?>>>> GetInvoiceRecordsAsync(DateTime? monthStart, int? locationId, bool noLocation,
            string? bucket, int? customerId, string? status, DateTime businessNow, CeoInvoiceStatusIds statuses, int top);

        Task<List<List<IDictionary<string, object?>>>> GetSpaceRecordsAsync(int? spaceTypeId, int? locationId, bool noLocation,
            bool? occupied, DateTime businessNow, int top);

        Task<List<List<IDictionary<string, object?>>>> GetBookingRecordsAsync(DateTime? from, DateTime? to, int? locationId, bool noLocation,
            string? statusIds, bool runningOnly, DateTime businessNow, int top);

        Task<List<List<IDictionary<string, object?>>>> GetInquiryRecordsAsync(DateTime? from, DateTime? to, string? inquiryType, int top);

        Task<List<List<IDictionary<string, object?>>>> GetQuotationRecordsAsync(DateTime? from, DateTime? to, int? userId, string? stage,
            int? locationId, int top);
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
    }

    /// <summary>The CEO dashboard procedures (script CEO_01) are not on this database yet.</summary>
    public sealed class CeoDatabaseUpdateMissingException : Exception
    {
        public const string UserMessage = "The CEO dashboard needs the database update CEO_01.";

        public CeoDatabaseUpdateMissingException(Exception inner) : base(UserMessage, inner)
        {
        }
    }
}
