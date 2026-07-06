using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IBranchService
    {
        Task<ApiResponse> GetAllBranchesAsync();
        Task<ApiResponse> GetAllCompaniesAsync();
        Task<ApiResponse> GetAllCitiesAsync();
    }
}
