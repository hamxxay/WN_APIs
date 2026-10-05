using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    public interface IHikAccessService
    {
        Task<HikAccessDashboardDto> GetDashboardAsync(int expiringDays = 7);
        Task<IEnumerable<HikAccessEventDto>> GetEventsAsync(DateTime? from, DateTime? to, int? deviceId, string? employeeNo, string? name, int limit = 500);
        Task<IEnumerable<HikSyncActivityDto>> GetSyncActivityAsync(int limit = 200);
        Task<HikAccessAnalyticsDto> GetAnalyticsAsync(DateTime from, DateTime to);
        Task<HikUserAnalyticsDto> GetUserAnalyticsAsync(string? employeeNo, string? name, DateTime from, DateTime to);
    }
}
