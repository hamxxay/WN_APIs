using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IDashboardService
    {
        Task<ApiResponse> GetSummaryAsync();
        Task<DashboardOverviewDto> GetOverviewAsync(int? locationId, string? period = null);

        /// <summary>
        /// Admin sidebar badges: counts of items waiting on staff, keyed by sidebar route ("/admin/bookings" ...).
        /// Machines offline ("/admin/access-dashboard") only when <paramref name="includeMachines"/>.
        /// </summary>
        /// <summary>Items waiting on staff that this user has not marked as read, per sidebar route.</summary>
        Task<Dictionary<string, int>> GetNavBadgesAsync(int? locationId, bool includeMachines, string? userEmail);
        /// <summary>Marks every item currently waiting on the given routes as read for this user. False if the table is missing.</summary>
        Task<bool> MarkNavBadgesReadAsync(int? locationId, string userEmail, IEnumerable<string> routes);
    }
}
