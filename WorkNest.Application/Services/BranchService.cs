using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class BranchService : IBranchService
    {
        private readonly IDbRepository _db;
        public BranchService(IDbRepository db) => _db = db;

        public async Task<ApiResponse> GetAllBranchesAsync()
        {
            var result = await _db.GetAllBranchesAsync();
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetAllCompaniesAsync()
        {
            var result = await _db.GetAllCompaniesAsync();
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetAllCitiesAsync()
        {
            var result = await _db.GetAllCitiesAsync();
            return ApiResponse.Ok(result);
        }
    }
}
