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
            var tasks = await Task.WhenAll(
                _db.GetAllUsersAsync(),
                _db.GetAllSpacesAsync(),
                _db.GetAllBookingsAsync(),
                _db.GetAllContactsAsync(),
                _db.GetAllLocationsAsync(),
                _db.GetAllPricingPlansAsync(),
                _db.GetAllGalleryImagesAsync(),
                _db.GetAllMembershipsAsync()
            );

            return ApiResponse.Ok(new
            {
                users       = tasks[0].Count(),
                spaces      = tasks[1].Count(),
                bookings    = tasks[2].Count(),
                contacts    = tasks[3].Count(),
                locations   = tasks[4].Count(),
                plans       = tasks[5].Count(),
                gallery     = tasks[6].Count(),
                memberships = tasks[7].Count(),
            });
        }
    }
}
