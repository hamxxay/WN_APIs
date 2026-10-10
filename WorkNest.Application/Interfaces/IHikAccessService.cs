using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    public interface IHikAccessService
    {
        /// <summary><paramref name="locationIds"/> limits the machines (and their counts) to those locations; null = all.</summary>
        Task<HikAccessDashboardDto> GetDashboardAsync(int expiringDays = 7, IReadOnlyCollection<int>? locationIds = null);
        Task<IEnumerable<HikAccessEventDto>> GetEventsAsync(DateTime? from, DateTime? to, int? deviceId, string? employeeNo, string? name, int limit = 500, IReadOnlyCollection<int>? locationIds = null);
        Task<IEnumerable<HikSyncActivityDto>> GetSyncActivityAsync(int limit = 200, IReadOnlyCollection<int>? locationIds = null);
        Task<HikAccessAnalyticsDto> GetAnalyticsAsync(DateTime from, DateTime to, IReadOnlyCollection<int>? locationIds = null);
        Task<HikUserAnalyticsDto> GetUserAnalyticsAsync(string? employeeNo, string? name, DateTime from, DateTime to, IReadOnlyCollection<int>? locationIds = null);
    }
}
