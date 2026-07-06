using WorkNest.Application.DTOs.SpaceConfig;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface ISpaceConfigService
    {
        Task<ApiResponse> GetSpaceConfigAsync();
        Task<ApiResponse> GetSecurityDepositAsync(string category);
        Task<ApiResponse> UpdateSpaceConfigAsync(string category, SpaceConfigUpdateRequest request, string? adminEmail);
        Task<ApiResponse> GenerateInventoryAsync(SpaceInventoryRequest request);
    }
}
