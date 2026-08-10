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
    }
}
