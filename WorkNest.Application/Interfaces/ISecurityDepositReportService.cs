using WorkNest.Application.DTOs.Reports;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Security deposit report. <c>locationIds</c> = the caller's locations (location-bound staff: only the deposits whose
    /// booking is in them, only their customers in the lookup); null = every location. Before SEC_LOC_02 has run a scoped
    /// call throws <see cref="WorkNest.Application.Helpers.LocationScopeUpdateMissingException"/> (503).
    /// </summary>
    public interface ISecurityDepositReportService
    {
        Task<ApiResponse> GetCustomerSummaryAsync(SecurityDepositReportFilterDto filter, IReadOnlyCollection<int>? locationIds = null);
        Task<ApiResponse> GetCustomerDetailAsync(int customerId, DateTime? fromDate, DateTime? toDate, IReadOnlyCollection<int>? locationIds = null);
        Task<byte[]> ExportExcelAsync(SecurityDepositReportFilterDto filter, IReadOnlyCollection<int>? locationIds = null);
        Task<byte[]> ExportCustomerDetailExcelAsync(int customerId, DateTime? fromDate, DateTime? toDate, IReadOnlyCollection<int>? locationIds = null);
        Task<ApiResponse> GetCustomerLookupAsync(IReadOnlyCollection<int>? locationIds = null);
    }
}
