using WorkNest.Application.DTOs.PricingPlan;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class PricingPlanService : IPricingPlanService
    {
        private readonly IDbRepository _db;
        public PricingPlanService(IDbRepository db) => _db = db;

        public async Task<IEnumerable<object>> GetAllPlansAsync() =>
            (await _db.GetAllPricingPlansAsync()).Cast<object>();

        public async Task<ApiResponse> GetPlanSummaryAsync(int id)
        {
            var plans = await _db.GetAllPricingPlansAsync();
            var plan  = plans.FirstOrDefault(p =>
                p.TryGetValue("id", out var pid) && Convert.ToInt32(pid) == id);
            if (plan is null) return ApiResponse.Fail("Plan not found");

            var memberships = (await _db.GetMembershipsByPlanIdAsync(id)).ToList();
            var price       = plan.TryGetValue("price", out var pr) ? Convert.ToDouble(pr) : 0;

            return ApiResponse.Ok(new
            {
                plan,
                stats = new
                {
                    totalSubscribers     = memberships.Count,
                    activeSubscribers    = memberships.Count(m => m.TryGetValue("status", out var s) && s?.ToString() == "Active"),
                    pausedSubscribers    = memberships.Count(m => m.TryGetValue("status", out var s) && s?.ToString() == "Paused"),
                    expiredSubscribers   = 0,
                    cancelledSubscribers = memberships.Count(m => m.TryGetValue("status", out var s) && s?.ToString() == "Cancelled"),
                    paidRevenue          = memberships.Count * price,
                },
                recentMemberships = memberships.Take(10),
                recentPayments    = Array.Empty<object>(),
            });
        }

        public async Task<ApiResponse> CreatePlanAsync(PricingPlanUpsertRequest request)
        {
            var id = await _db.CreatePricingPlanAsync(request.Name, request.Price,
                request.BillingCycle, request.IncludesHours ?? 0, request.IsActive ?? true);
            return ApiResponse.Ok(new { id }, "Pricing plan created.");
        }

        public async Task<ApiResponse> UpdatePlanAsync(int id, PricingPlanUpsertRequest request)
        {
            await _db.UpdatePricingPlanAsync(id, request.Name, request.Price,
                request.BillingCycle, request.IncludesHours ?? 0, request.IsActive ?? true);
            return ApiResponse.Ok("Pricing plan updated.");
        }

        public async Task<ApiResponse> DeletePlanAsync(int id)
        {
            await _db.SoftDeletePricingPlanAsync(id);
            return ApiResponse.Ok("Pricing plan deleted.");
        }
    }
}
