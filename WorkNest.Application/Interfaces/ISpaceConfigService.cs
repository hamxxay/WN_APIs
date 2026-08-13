using WorkNest.Application.DTOs.SpaceConfig;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface ISpaceConfigService
    {
        Task<ApiResponse> GetSpaceConfigAsync();
        Task<ApiResponse> GetSpaceConfigV2Async(int? companyId, int? branchId, int? locationId);
        Task<ApiResponse> CreateSpaceConfigV2Async(SpaceConfigV2Request request, string? userEmail);
        Task<ApiResponse> UpdateSpaceConfigV2Async(int id, SpaceConfigV2Request request, string? userEmail);
        Task<ApiResponse> DeleteSpaceConfigV2Async(int id);
        Task<ApiResponse> GetSecurityDepositAsync(string category);
        Task<ApiResponse> UpdateSpaceConfigAsync(string category, SpaceConfigUpdateRequest request, string? updatedBy);
        Task<ApiResponse> GenerateInventoryAsync(SpaceInventoryRequest request);
        Task<ApiResponse> DeleteSpacesFromConfigAsync(DeleteSpacesFromConfigRequest request);
    }
}
