using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    public interface IHikDeviceService
    {
        Task<IEnumerable<HikDeviceDto>> GetDevicesAsync(string? location = null);
        Task<HikRosterResponseDto> GetRostersAsync(string? location = null, bool includeMachineAdmins = true);
        Task<HikDeviceUsersResponseDto> GetDeviceUsersAsync(int deviceId, bool includeMachineAdmins = true);
        Task<HikNextEmployeeNoResponseDto> GetNextEmployeeNoAsync();
    }
}
