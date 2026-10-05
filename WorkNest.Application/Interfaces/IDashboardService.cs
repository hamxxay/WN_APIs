using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IDashboardService
    {
        Task<ApiResponse> GetSummaryAsync();
        Task<DashboardOverviewDto> GetOverviewAsync(int? locationId);
    }
}
