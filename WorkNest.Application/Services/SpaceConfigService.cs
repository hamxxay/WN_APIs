using WorkNest.Application.DTOs.SpaceConfig;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class SpaceConfigService : ISpaceConfigService
    {
        private readonly IDbRepository _db;
        public SpaceConfigService(IDbRepository db) => _db = db;

        // ── Legacy ────────────────────────────────────────────
        public async Task<ApiResponse> GetSpaceConfigAsync()
        {
            var result = await _db.GetSpaceConfigAsync();
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetSecurityDepositAsync(string category)
        {
            var deposit = await _db.GetSecurityDepositAsync(category);
            return ApiResponse.Ok(new { spaceCategory = category, securityDeposit = deposit });
        }

        public async Task<ApiResponse> UpdateSpaceConfigAsync(string category,
            SpaceConfigUpdateRequest request, string? adminEmail)
        {
            await _db.UpdateSpaceConfigAsync(category, request.TotalSpaces,
                request.DefaultCapacities, request.OpeningTime, request.ClosingTime,
                adminEmail, request.SecurityDeposit);
            return ApiResponse.Ok($"Space config for '{category}' updated.");
        }

        public async Task<ApiResponse> GenerateInventoryAsync(SpaceInventoryRequest request)
        {
            var result = await _db.GenerateSpaceInventoryAsync(request.SpaceCategory,
                request.SpaceTypeId, request.LocationId,
                request.PricePerHour ?? 0, request.PricePerDay ?? 0, request.PricePerMonth ?? 0,
                request.Amenities);
            return ApiResponse.Ok(result, "Inventory generated/synced.");
        }

        // ── Multi-location ────────────────────────────────────
        public async Task<ApiResponse> GetConfigsAsync(int? companyId, int? branchId, int? locationId)
        {
            var result = await _db.GetSpaceConfigsV2Async(companyId, branchId, locationId);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> CreateConfigAsync(SpaceConfigCreateRequest request, string? adminEmail)
        {
            var newId = await _db.CreateSpaceConfigAsync(
                request.SpaceCategory, request.TotalSpaces, request.CodePrefix, request.MinCode,
                request.OpeningTime, request.ClosingTime, request.SecurityDeposit,
                request.RentAccountId, request.DepositAccountId, request.FloorId,
                request.PricePerHour, request.PricePerDay, request.PricePerMonth,
                request.Amenities, request.LocationId, request.BranchId, request.CompanyId,
                request.SpaceTypeId, adminEmail);
            return ApiResponse.Ok(new { id = newId }, "Configuration created.");
        }

        public async Task<ApiResponse> UpdateConfigAsync(int id, SpaceConfigEditRequest request, string? adminEmail)
        {
            await _db.UpdateSpaceConfigV2Async(id,
                request.TotalSpaces, request.CodePrefix, request.MinCode,
                request.OpeningTime, request.ClosingTime, request.SecurityDeposit,
                request.RentAccountId, request.DepositAccountId, request.FloorId,
                request.PricePerHour, request.PricePerDay, request.PricePerMonth,
                request.Amenities, adminEmail);
            return ApiResponse.Ok("Configuration updated.");
        }

        public async Task<ApiResponse> DeleteConfigAsync(int id)
        {
            await _db.DeleteSpaceConfigAsync(id);
            return ApiResponse.Ok("Configuration deleted.");
        }

        public async Task<ApiResponse> GenerateSpacesAsync(int configId)
        {
            var result = await _db.GenerateSpacesFromConfigAsync(configId);
            return ApiResponse.Ok(result, "Spaces generated.");
        }

        public async Task<ApiResponse> GetSpaceStatusAsync(int configId)
        {
            var result = await _db.GetSpaceStatusForConfigAsync(configId);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> DeleteSpacesAsync(DeleteSpacesRequest request)
        {
            var blocked = await _db.DeleteSpacesFromConfigAsync(request.ConfigId, request.SpaceGuids);
            var blockedList = blocked.ToList();
            if (blockedList.Any())
                return ApiResponse.Ok(new { blocked = blockedList },
                    $"{blockedList.Count} space(s) could not be deleted due to existing bookings.");
            return ApiResponse.Ok("Spaces deleted successfully.");
        }
    }
}
