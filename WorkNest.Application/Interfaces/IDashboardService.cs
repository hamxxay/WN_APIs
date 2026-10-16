using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IDashboardService
    {
        Task<ApiResponse> GetSummaryAsync();
        /// <param name="locationIds">Locations to total; null = all locations.</param>
        Task<DashboardOverviewDto> GetOverviewAsync(IReadOnlyCollection<int>? locationIds, string? period = null);
        /// <summary>
        /// The rooms behind the overview's Occupancy card: every active space in use right now (same rule, so the count is
        /// the card's "X of Y"), with the booking that covers it, sorted by location then room.
        /// </summary>
        /// <param name="locationIds">Locations to list; null = all locations.</param>
        Task<DashboardOccupiedSpacesDto> GetOccupiedSpacesAsync(IReadOnlyCollection<int>? locationIds);

        /// <summary>
        /// Admin sidebar badges: counts of items waiting on staff, keyed by sidebar route ("/admin/bookings" ...).
        /// Machines offline ("/admin/access-dashboard") only when <paramref name="includeMachines"/>.
        /// </summary>
        /// <summary>Items waiting on staff that this user has not marked as read, per sidebar route.</summary>
        /// <param name="locationIds">Location-bound staff's locations (all they are assigned to, as their lists show) for
        /// bookings, quotations, agreements, invoices, KYC, door access and machines; null = every location.</param>
        /// <param name="branchLocationIds">Location-bound staff's locations: tour inquiries and complaints of those branches plus
        /// the ones without a branch; null = all.</param>
        Task<Dictionary<string, int>> GetNavBadgesAsync(IReadOnlyCollection<int>? locationIds, bool includeMachines, string? userEmail,
            IReadOnlyCollection<int>? branchLocationIds = null);
        /// <summary>Marks every item currently waiting on the given routes as read for this user. False if the table is missing.</summary>
        Task<bool> MarkNavBadgesReadAsync(IReadOnlyCollection<int>? locationIds, string userEmail, IEnumerable<string> routes,
            IReadOnlyCollection<int>? branchLocationIds = null);
    }
}
