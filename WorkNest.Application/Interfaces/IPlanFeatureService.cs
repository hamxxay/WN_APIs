using WorkNest.Application.DTOs.PlanFeature;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IPlanFeatureService
    {
        Task<ApiResponse> GetByPlanAsync(int planId);
        Task<ApiResponse> CreateAsync(PlanFeatureRequest request);
        Task<ApiResponse> UpdateAsync(int id, PlanFeatureRequest request);
        Task<ApiResponse> DeleteAsync(int id);
    }
}
