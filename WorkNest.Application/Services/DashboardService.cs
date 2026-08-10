using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly IDbRepository _db;
        public DashboardService(IDbRepository db) => _db = db;

        public async Task<ApiResponse> GetSummaryAsync()
        {
            var results = await _db.GetDashboardSummaryAsync();
            return ApiResponse.Ok(results);
        }
    }
}
