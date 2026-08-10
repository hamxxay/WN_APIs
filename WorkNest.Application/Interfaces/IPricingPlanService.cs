using WorkNest.Application.DTOs.PricingPlan;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IPricingPlanService
    {
        Task<IEnumerable<object>> GetAllPlansAsync();
        Task<(IEnumerable<object> Items, int Total)> GetPlansAsync(int page, int limit);
        Task<ApiResponse> GetPlanSummaryAsync(int id);
        Task<ApiResponse> CreatePlanAsync(PricingPlanUpsertRequest request, int? actorId);
        Task<ApiResponse> UpdatePlanAsync(int id, PricingPlanUpsertRequest request);
        Task<ApiResponse> DeletePlanAsync(int id);
    }
}
