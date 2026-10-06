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
        /// Super admins see all locations (or the one asked for); other roles only their own location.
        /// </summary>
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
        [HttpGet("api/dashboard/overview")]
        public async Task<IActionResult> Overview([FromQuery] int? locationId, [FromQuery] string? period)
        {
            var scope = User.IsSuperAdmin() ? (locationId > 0 ? locationId : null) : User.GetLocationId();
            return Ok(await _dashboard.GetOverviewAsync(scope, period));
        }

        // Sidebar routes a sales executive may open (same list as SALES_EXECUTIVE_ROUTES in WorkNest_FE admin.guard.ts).
        private static readonly HashSet<string> SalesExecutiveRoutes = new(StringComparer.OrdinalIgnoreCase)
        {
            "/admin/kyc", "/admin/quotations", "/admin/agreements", "/admin/lease-templates", "/admin/invoices",
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

            var scope = User.IsSuperAdmin() ? null : User.GetLocationId();
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

            var scope = User.IsSuperAdmin() ? null : User.GetLocationId();
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
