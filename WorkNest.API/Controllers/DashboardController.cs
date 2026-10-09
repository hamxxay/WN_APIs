using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.Interfaces;
using WorkNest.API.Extensions;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _dashboard;
        public DashboardController(IDashboardService dashboard) => _dashboard = dashboard;

        [HttpGet("api/dashboard/summary")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")]
        public async Task<IActionResult> Summary() =>
            Ok(await _dashboard.GetSummaryAsync());

        /// <summary>
        /// Admin dashboard: headline numbers, 6-month trend and "needs attention" lists.
        /// locationIds (comma-separated) or locationId: the locations to total; none = all locations.
        /// Sales executives only ever see their own locations (WN_Users.LocationId + WN_UserLocations): asked-for
        /// locations outside them are ignored, and none (or none of theirs) means all of theirs.
        /// </summary>
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]
        [HttpGet("api/dashboard/overview")]
        public async Task<IActionResult> Overview([FromQuery] int? locationId, [FromQuery] string? locationIds, [FromQuery] string? period)
        {
            var requested = (locationIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var id) ? id : 0)
                .Append(locationId ?? 0)
                .Where(id => id > 0).Distinct().ToList();

            List<int>? scope;
            if (User.IsLocationBoundRole())
            {
                var allowed = User.GetLocationIds();
                var mine = requested.Where(allowed.Contains).ToList();
                scope = mine.Count > 0 ? mine : (allowed.Count > 0 ? allowed.ToList() : new List<int> { -1 }); // -1 matches nothing
            }
            else
            {
                scope = requested.Count > 0 ? requested : null;
            }
            return Ok(await _dashboard.GetOverviewAsync(scope, period));
        }

        // Sidebar routes a sales executive may open (same list as SALES_EXECUTIVE_ROUTES in WorkNest_FE admin.guard.ts).
        private static readonly HashSet<string> SalesExecutiveRoutes = new(StringComparer.OrdinalIgnoreCase)
        {
            "/admin", "/admin/dashboard", "/admin/kyc", "/admin/quotations", "/admin/agreements", "/admin/lease-templates", "/admin/invoices",
            "/admin/bookings", "/admin/attendants", "/admin/staff-access", "/admin/biometric-users", "/admin/machine-users",
            "/admin/contacts", "/admin/network", "/admin/network/clients", "/admin/network/devices",
            "/admin/network/internet", "/admin/network/wifi"
        };

        /// <summary>
        /// Admin sidebar badges: counts of items waiting on staff right now, keyed by sidebar route
        /// (e.g. { "/admin/bookings": 2 }). Polled every 60 s by every open admin tab, so it is one cheap query.
        /// Only routes the caller's role can open are returned; machines offline is admin / super admin only.
        /// Super admins see every location; other roles only their own location where the data has one.
        /// </summary>
        [HttpGet("api/admin/nav-badges")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")]
        public async Task<IActionResult> NavBadges()
        {
            var isAdmin = User.IsAdminOrSuperAdmin();
            var isSalesExecutive = User.IsInRole("sales_executive") || User.IsInRole("SalesExecutive")
                                   || User.GetRole() == WorkNest.Common.Constants.Roles.SalesExecutive;
            if (!isAdmin && !isSalesExecutive)
                return Ok(new Dictionary<string, int>()); // e.g. receptionist: no admin sidebar routes

            var scope = User.IsLocationBoundRole() ? User.GetLocationId() : null;
            var badges = await _dashboard.GetNavBadgesAsync(scope, includeMachines: isAdmin, User.GetEmail());
            if (!isAdmin)
                badges = badges.Where(kv => SalesExecutiveRoutes.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            return Ok(badges);
        }

        public class MarkNavBadgesReadRequest
        {
            /// <summary>Sidebar routes to mark read (e.g. "/admin/bookings"); empty = every badge this user can see.</summary>
            public List<string>? Routes { get; set; }
        }

        /// <summary>
        /// "Mark as read": remembers the items waiting now on the given routes for this user (any device),
        /// so their badges count only items that arrive afterwards.
        /// </summary>
        [HttpPost("api/admin/nav-badges/read")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]
        public async Task<IActionResult> MarkNavBadgesRead([FromBody] MarkNavBadgesReadRequest? request)
        {
            var email = User.GetEmail();
            if (string.IsNullOrWhiteSpace(email)) return Unauthorized();

            var isAdmin = User.IsAdminOrSuperAdmin();
            var allowed = isAdmin
                ? new[] { "/admin/agreements", "/admin/contacts", "/admin/bookings", "/admin/quotations", "/admin/kyc",
                          "/admin/invoices", "/admin/attendants", "/admin/access-dashboard" }
                : SalesExecutiveRoutes.ToArray();
            var routes = request?.Routes is { Count: > 0 } r
                ? r.Where(x => allowed.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList()
                : allowed.ToList();

            var scope = User.IsLocationBoundRole() ? User.GetLocationId() : null;
            if (!await _dashboard.MarkNavBadgesReadAsync(scope, email, routes))
                return StatusCode(503, new { isSuccessful = false, message = "Mark as read is not set up yet: the WN_NAV_BadgeReads table is missing." });
            return Ok(new { isSuccessful = true });
        }

        [HttpGet("/")]
        [AllowAnonymous]
        public IActionResult Root() =>
            Ok(new { app = "WorkNest ASP.NET Core API", status = "healthy" });
    }
}
