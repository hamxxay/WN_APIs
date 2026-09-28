using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    public interface IHikDeviceService
    {
        Task<IEnumerable<HikDeviceDto>> GetDevicesAsync(string? location = null);
        Task<HikRosterResponseDto> GetRostersAsync(string? location = null);
        Task<HikDeviceUsersResponseDto> GetDeviceUsersAsync(int deviceId);
        Task<HikNextEmployeeNoResponseDto> GetNextEmployeeNoAsync();
    }
}
