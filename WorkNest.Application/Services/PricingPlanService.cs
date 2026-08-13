using WorkNest.Application.DTOs.PricingPlan;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class PricingPlanService : IPricingPlanService
    {
        private readonly IDbRepository _db;
        public PricingPlanService(IDbRepository db) => _db = db;

        public async Task<IEnumerable<object>> GetAllPlansAsync()
        {
            var plans = await _db.GetAllPricingPlansAsync();
            var result = new List<object>();
            foreach (var plan in plans)
            {
                var id = Convert.ToInt32(plan["Id"]);
                var features = await _db.GetPlanFeaturesByPlanIdAsync(id);
                var planDict = new Dictionary<string, object?>(plan);
                planDict["features"] = features;
                result.Add(planDict);
            }
            return result;
        }

        public async Task<(IEnumerable<object> Items, int Total)> GetPlansAsync(int page, int limit)
        {
            var (rows, total) = await _db.GetPricingPlansAsync(page, limit);
            var result = new List<object>();
            foreach (var plan in rows)
            {
                var id = Convert.ToInt32(plan["Id"]);
                var features = await _db.GetPlanFeaturesByPlanIdAsync(id);
                var planDict = new Dictionary<string, object?>(plan);
                planDict["features"] = features;
                result.Add(planDict);
            }
            return (result, total);
        }

        public async Task<ApiResponse> GetPlanSummaryAsync(int id)
        {
            var plan = await _db.GetPricingPlanSummaryAsync(id);
            if (plan is null) return ApiResponse.Fail("Plan not found");
            var features = await _db.GetPlanFeaturesByPlanIdAsync(id);
            var planDict = new Dictionary<string, object?>(plan);
            planDict["features"] = features;
            return ApiResponse.Ok(planDict);
        }

        public async Task<ApiResponse> CreatePlanAsync(PricingPlanUpsertRequest request, int? actorId)
        {
            var (id, publicId) = await _db.InsertPricingPlanAsync(
                request.Name, request.Description, request.BillingPeriodId,
                request.Price, request.IncludesHours, request.CurrencyCode, actorId);
            return ApiResponse.Ok(new { id, publicId }, "Pricing plan created.");
        }

        public async Task<ApiResponse> UpdatePlanAsync(int id, PricingPlanUpsertRequest request)
        {
            await _db.UpdatePricingPlanAsync(id, request.Name, request.Description,
                request.BillingPeriodId, request.Price, request.IncludesHours, request.CurrencyCode);
            return ApiResponse.Ok("Pricing plan updated.");
        }

        public async Task<ApiResponse> DeletePlanAsync(int id)
        {
            await _db.DeletePricingPlanAsync(id);
            return ApiResponse.Ok("Pricing plan deleted.");
        }
    }
}
