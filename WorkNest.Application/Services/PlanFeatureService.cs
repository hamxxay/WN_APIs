using WorkNest.Application.DTOs.PlanFeature;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class PlanFeatureService : IPlanFeatureService
    {
        private readonly IDbRepository _db;
        public PlanFeatureService(IDbRepository db) => _db = db;

        public async Task<ApiResponse> GetByPlanAsync(int planId)
        {
            var rows = await _db.GetPlanFeaturesByPlanIdAsync(planId);
            return ApiResponse.Ok(rows);
        }

        public async Task<ApiResponse> CreateAsync(PlanFeatureRequest request)
        {
            var id = await _db.CreatePlanFeatureAsync(request.PlanId, request.FeatureName);
            return ApiResponse.Ok(new { id }, "Feature created.");
        }

        public async Task<ApiResponse> UpdateAsync(int id, PlanFeatureRequest request)
        {
            await _db.UpdatePlanFeatureAsync(id, request.FeatureName);
            return ApiResponse.Ok("Feature updated.");
        }

        public async Task<ApiResponse> DeleteAsync(int id)
        {
            await _db.SoftDeletePlanFeatureAsync(id);
            return ApiResponse.Ok("Feature deleted.");
        }
    }
}
