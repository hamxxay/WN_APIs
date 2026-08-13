using WorkNest.Application.DTOs.SpaceConfig;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class SpaceConfigService : ISpaceConfigService
    {
        private readonly IDbRepository _db;
        public SpaceConfigService(IDbRepository db) => _db = db;

        public async Task<ApiResponse> GetSpaceConfigAsync()
        {
            var result = await _db.GetSpaceConfigAsync();
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetSpaceConfigV2Async(int? companyId, int? branchId, int? locationId)
        {
            var result = await _db.GetSpaceConfigV2Async(companyId, branchId, locationId);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> CreateSpaceConfigV2Async(SpaceConfigV2Request request, string? userEmail)
        {
            var newId = await _db.InsertSpaceConfigV2Async(request, userEmail);
            return ApiResponse.Ok(new { id = newId }, "Config created.");
        }

        public async Task<ApiResponse> UpdateSpaceConfigV2Async(int id, SpaceConfigV2Request request, string? userEmail)
        {
            await _db.UpdateSpaceConfigV2Async(id, request, userEmail);
            return ApiResponse.Ok($"Space config #{id} updated.");
        }

        public async Task<ApiResponse> DeleteSpaceConfigV2Async(int id)
        {
            await _db.DeleteSpaceConfigV2Async(id);
            return ApiResponse.Ok($"Space config #{id} deleted.");
        }

        public async Task<ApiResponse> GetSecurityDepositAsync(string category)
        {
            var deposit = await _db.GetSecurityDepositAsync(category);
            return ApiResponse.Ok(new { spaceCategory = category, securityDeposit = deposit });
        }

        public async Task<ApiResponse> UpdateSpaceConfigAsync(string category, SpaceConfigUpdateRequest request, string? updatedBy)
        {
            await _db.UpdateSpaceConfigAsync(
                category,
                updatedBy ?? request.UpdatedBy,
                request.TotalSpaces,
                request.DefaultCapacities,
                request.OpeningTime,
                request.ClosingTime,
                request.SecurityDeposit,
                request.PricePerHour,
                request.PricePerDay,
                request.PricePerMonth);
            return ApiResponse.Ok($"Space config for '{category}' updated.");
        }

        public async Task<ApiResponse> GenerateInventoryAsync(SpaceInventoryRequest request)
        {
            var result = await _db.GenerateSpaceInventoryAsync(
                request.LocationId, request.SpaceTypeId,
                request.CodePrefix, request.MinCode, request.TotalSpaces);
            return ApiResponse.Ok(result, "Inventory generated.");
        }

        public async Task<ApiResponse> DeleteSpacesFromConfigAsync(DeleteSpacesFromConfigRequest request)
        {
            var (deleted, blocked) = await _db.DeleteSpacesFromConfigAsync(request.ConfigId, request.SpaceGuids);
            return ApiResponse.Ok(new { deleted, blocked }, $"{deleted.Count} space(s) deleted.");
        }
    }
}
