using WorkNest.Application.DTOs.Reports;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface ISecurityDepositReportService
    {
        Task<ApiResponse> GetCustomerSummaryAsync(SecurityDepositReportFilterDto filter);
        Task<ApiResponse> GetCustomerDetailAsync(int customerId, DateTime? fromDate, DateTime? toDate);
        Task<byte[]> ExportExcelAsync(SecurityDepositReportFilterDto filter);
        Task<byte[]> ExportCustomerDetailExcelAsync(int customerId, DateTime? fromDate, DateTime? toDate);
        Task<ApiResponse> GetCustomerLookupAsync();
    }
}
