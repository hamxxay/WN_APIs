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
    }
}
