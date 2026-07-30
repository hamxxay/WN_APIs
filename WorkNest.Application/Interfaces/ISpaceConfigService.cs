using WorkNest.Application.DTOs.SpaceConfig;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface ISpaceConfigService
    {
        // Legacy
        Task<ApiResponse> GetSpaceConfigAsync();
        Task<ApiResponse> GetSecurityDepositAsync(string category);
        Task<ApiResponse> UpdateSpaceConfigAsync(string category, SpaceConfigUpdateRequest request, string? adminEmail);
        Task<ApiResponse> GenerateInventoryAsync(SpaceInventoryRequest request);

        // Multi-location
        Task<ApiResponse> GetConfigsAsync(int? companyId, int? branchId, int? locationId);
        Task<ApiResponse> CreateConfigAsync(SpaceConfigCreateRequest request, string? adminEmail);
        Task<ApiResponse> UpdateConfigAsync(int id, SpaceConfigEditRequest request, string? adminEmail);
        Task<ApiResponse> DeleteConfigAsync(int id);
        Task<ApiResponse> GenerateSpacesAsync(int configId);
        Task<ApiResponse> GetSpaceStatusAsync(int configId);
        Task<ApiResponse> DeleteSpacesAsync(DeleteSpacesRequest request);
    }
}
