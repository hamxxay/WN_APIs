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
    }
}
