using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Building staff (janitors, office boys, …) on the Hikvision machines — no booking needed.
    /// <c>callerLocationIds</c>: the caller's locations when they are location-bound (their changes then never reach a
    /// machine of another location), null for super admins.
    /// </summary>
    public interface IHikStaffService
    {
        /// <summary><paramref name="locationIds"/>: only staff on machines of those locations (null = all).</summary>
        Task<IEnumerable<HikStaffDto>> GetStaffAsync(bool includeMachineAdmins = true, IReadOnlyCollection<int>? locationIds = null);
        /// <summary>
        /// Locations of the machines a staff member is on (last roster) or has changes queued for; machines without a
        /// location yet are left out, so an empty set means "no location".
        /// </summary>
        Task<HashSet<int>> GetStaffLocationIdsAsync(HikStaffDto staff);
        /// <summary>True when every id is a machine of those locations or one without a location yet.</summary>
        Task<bool> DevicesInLocationsAsync(IEnumerable<int> deviceIds, IReadOnlyCollection<int> locationIds);
        Task<IEnumerable<HikTagDto>> GetTagsAsync();
        Task<HikTagDto> AddTagAsync(string name);
        Task<HikStaffResultDto> CreateStaffAsync(HikStaffCreateRequest request);
        Task<HikStaffResultDto> UpdateStaffMachinesAsync(string employeeNo, HikStaffMachinesRequest request);
        Task<HikAccessSyncResultDto> SetStaffEnabledAsync(string employeeNo, bool isEnabled, IReadOnlyCollection<int>? callerLocationIds = null);
        Task<HikStaffResultDto> DeleteStaffAsync(string employeeNo, IReadOnlyCollection<int>? callerLocationIds = null);
        Task<HikEnrollResultDto> EnrollStaffAsync(string employeeNo, string type, HikStaffEnrollRequest request, IReadOnlyCollection<int>? callerLocationIds = null);
    }
}
