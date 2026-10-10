using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Building staff (janitors, office boys, …) on the Hikvision machines — no booking needed.
    /// </summary>
    public interface IHikStaffService
    {
        /// <summary><paramref name="locationIds"/>: only staff on machines of those locations (null = all).</summary>
        Task<IEnumerable<HikStaffDto>> GetStaffAsync(bool includeMachineAdmins = true, IReadOnlyCollection<int>? locationIds = null);
        Task<IEnumerable<HikTagDto>> GetTagsAsync();
        Task<HikTagDto> AddTagAsync(string name);
        Task<HikStaffResultDto> CreateStaffAsync(HikStaffCreateRequest request);
        Task<HikStaffResultDto> UpdateStaffMachinesAsync(string employeeNo, HikStaffMachinesRequest request);
        Task<HikAccessSyncResultDto> SetStaffEnabledAsync(string employeeNo, bool isEnabled);
        Task<HikStaffResultDto> DeleteStaffAsync(string employeeNo);
        Task<HikEnrollResultDto> EnrollStaffAsync(string employeeNo, string type, HikStaffEnrollRequest request);
    }
}
