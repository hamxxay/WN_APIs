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
            var users     = await _db.GetAllUsersAsync();
            var spaces    = await _db.GetAllSpacesAsync();
            var bookings  = await _db.GetAllBookingsAsync();
            var contacts  = await _db.GetAllContactsAsync();
            var locations = await _db.GetAllLocationsAsync();
            var plans     = await _db.GetAllPricingPlansAsync();
            var gallery   = await _db.GetAllGalleryImagesAsync();
            var members   = await _db.GetAllMembershipsAsync();

            return ApiResponse.Ok(new
            {
                users       = users.Count(),
                spaces      = spaces.Count(),
                bookings    = bookings.Count(),
                contacts    = contacts.Count(),
                locations   = locations.Count(),
                plans       = plans.Count(),
                gallery     = gallery.Count(),
                memberships = members.Count(),
            });
        }
    }
}
