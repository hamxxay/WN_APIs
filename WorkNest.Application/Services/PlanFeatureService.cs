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

        public async Task<ApiResponse> CreateAsync(PlanFeatureRequest request, int? actorId)
        {
            var (id, publicId) = await _db.InsertPlanFeatureAsync(
                request.PlanId, request.FeatureName, request.FeatureValue, request.SortOrder);
            return ApiResponse.Ok(new { id, publicId }, "Feature created.");
        }

        public async Task<ApiResponse> UpdateAsync(int id, PlanFeatureUpdateRequest request)
        {
            await _db.UpdatePlanFeatureAsync(id, request.FeatureName, request.FeatureValue, request.SortOrder);
            return ApiResponse.Ok("Feature updated.");
        }

        public async Task<ApiResponse> DeleteAsync(int id)
        {
            await _db.DeletePlanFeatureAsync(id);
            return ApiResponse.Ok("Feature deleted.");
        }
    }
}
