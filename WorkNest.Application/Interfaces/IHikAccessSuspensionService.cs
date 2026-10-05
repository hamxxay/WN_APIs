using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Challan-based door access suspension: an overdue unpaid challan suspends the booking's access
    /// on the machines until it is paid; staff can extend access manually until a chosen date.
    /// </summary>
    public interface IHikAccessSuspensionService
    {
        /// <summary>Hourly cycle: open/close suspensions and block/unblock the affected bookings' people.</summary>
        Task<int> RunAccessSuspensionCycleAsync(CancellationToken cancellationToken = default);
        Task<HikAccessSuspensionDto> GetAccessSuspensionAsync(int bookingDetailId);
        Task<HikBookingChallansDto> GetBookingChallansAsync(int bookingDetailId);
        Task<HikAccessSyncResultDto> ExtendAccessAsync(int bookingDetailId, HikAccessExtendRequest request, string? createdByEmail);
        /// <summary>Admin dashboard: machines online/offline, queued operations, suspended bookings.</summary>
        Task<HikAccessOverviewDto> GetAccessOverviewAsync();
    }
}
