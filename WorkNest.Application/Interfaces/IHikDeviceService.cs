using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    public interface IHikDeviceService
    {
        Task<IEnumerable<HikDeviceDto>> GetDevicesAsync(string? location = null, IReadOnlyCollection<int>? locationIds = null);
        /// <summary>Super admin: the WorkNest location a machine belongs to (null = unassigned).</summary>
        Task<bool> SetDeviceLocationAsync(int deviceId, int? locationId);
        Task<HikRosterResponseDto> GetRostersAsync(string? location = null, bool includeMachineAdmins = true, IReadOnlyCollection<int>? locationIds = null);
        Task<HikDeviceUsersResponseDto> GetDeviceUsersAsync(int deviceId, bool includeMachineAdmins = true);
        Task<HikNextEmployeeNoResponseDto> GetNextEmployeeNoAsync();

        // Machine management (admin / super admin, HIK_DEV_01). Each throws HikDeviceManagementNotInstalledException
        // before HIK_DEV_01 has run.
        /// <summary>One machine for the edit form (no password); null when it doesn't exist.</summary>
        Task<HikDeviceDetailDto?> GetDeviceAsync(int deviceId);
        Task<List<HikDeviceGroupDto>> GetDeviceGroupsAsync();
        /// <summary>Adds a machine, then tests it (an offline machine is still saved). 400 / 409 when refused.</summary>
        Task<HikDeviceSaveOutcome> AddDeviceAsync(HikDeviceSaveRequest? request, string? by);
        /// <summary>Changes a machine (null fields kept, empty password kept), then tests it.</summary>
        Task<HikDeviceSaveOutcome> UpdateDeviceAsync(int deviceId, HikDeviceSaveRequest? request, string? by);
        /// <summary>Deletes a machine; 409 while its cached roster still has people, unless <paramref name="force"/>.</summary>
        Task<HikDeviceSaveOutcome> DeleteDeviceAsync(int deviceId, bool force, string? by);
    }

    /// <summary>
    /// The machine management procedures (dbo.WN_HIK_Device_Add / _Update / _Delete / _GetById / _GetGroups) are not on
    /// the database yet: SQL errors 2812 / 208. Answered 503 with <see cref="UserMessage"/>.
    /// </summary>
    public class HikDeviceManagementNotInstalledException : Exception
    {
        public const string UserMessage =
            "Database update needed: run the script HIK_DEV_01_device_management (Database/HikSync) to add, edit or delete machines.";

        public HikDeviceManagementNotInstalledException(Exception inner) : base(UserMessage, inner) { }
    }
}
