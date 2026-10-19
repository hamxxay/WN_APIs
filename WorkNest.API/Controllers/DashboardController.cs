using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.Interfaces;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _dashboard;
        private readonly IRoleDashboardService _roleDashboard;
        private readonly IForecastService _forecast;
        private readonly IAttendantService _attendants;
        public DashboardController(IDashboardService dashboard, IRoleDashboardService roleDashboard, IForecastService forecast, IAttendantService attendants)
        {
            _attendants = attendants;
            _dashboard = dashboard;
            _roleDashboard = roleDashboard;
            _forecast = forecast;
        }

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
        public async Task<IActionResult> Overview([FromQuery] int? locationId, [FromQuery] string? locationIds, [FromQuery] string? period) =>
            Ok(await _dashboard.GetOverviewAsync(Scope(locationId, locationIds), period));

        /// <summary>
        /// The rooms behind the overview's Occupancy card ("X of Y spaces in use"): one row per active space with a live
        /// booking right now (same rule as the card, so the count matches it), with location, space type, seats, customer,
        /// booking dates and status; sorted by location, then room. Same roles and location rules as the overview.
        /// </summary>
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]
        [HttpGet("api/dashboard/occupied-spaces")]
        public async Task<IActionResult> OccupiedSpaces([FromQuery] int? locationId, [FromQuery] string? locationIds) =>
            Ok(await _dashboard.GetOccupiedSpacesAsync(Scope(locationId, locationIds)));

        /// <summary>
        /// Role-based dashboard sections (same location rules as the overview). Sales executive: sales pipeline,
        /// tour inquiries &amp; follow-ups, renewals and collections. Admin: team &amp; service. Super admin: team &amp;
        /// service, location comparison, staff overview and system health (company-wide).
        /// </summary>
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]
        [HttpGet("api/dashboard/role")]
        public async Task<IActionResult> Role([FromQuery] int? locationId, [FromQuery] string? locationIds, [FromQuery] string? period)
        {
            var role = User.IsSuperAdmin() ? "super_admin"
                : User.IsAdminOrSuperAdmin() ? "admin"
                : "sales_executive";
            return Ok(await _roleDashboard.GetAsync(role, Scope(locationId, locationIds), period));
        }

        /// <summary>
        /// Revenue &amp; occupancy forecast for the next N months (default 3, at most 12) from current leases: expected
        /// rent, occupancy at month end, leases ending / starting, per month (see ForecastService for the rules).
        /// Same location rules as the overview; per-location rows when more than one location is in scope.
        /// The CEO may open it too (all locations, like a super admin); it is the only dashboard endpoint outside api/ceo it can use.
        /// </summary>
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive,ceo")]
        [CeoAllowed]
        [HttpGet("api/dashboard/forecast")]
        public async Task<IActionResult> Forecast([FromQuery] int? locationId, [FromQuery] string? locationIds, [FromQuery] int? months) =>
            Ok(await _forecast.GetAsync(Scope(locationId, locationIds), months ?? 3));

        /// <summary>
        /// Locations to total: the asked-for ones, none = all. Location-bound roles (admin, sales executive) only ever
        /// get their own locations: asked-for locations outside them are ignored, and none (or none of theirs) means
        /// all of theirs.
        /// </summary>
        private List<int>? Scope(int? locationId, string? locationIds)
        {
            var requested = (locationIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var id) ? id : 0)
                .Append(locationId ?? 0)
                .Where(id => id > 0).Distinct().ToList();

            if (User.IsLocationBoundRole())
            {
                var allowed = User.GetLocationIds();
                var mine = requested.Where(allowed.Contains).ToList();
                return mine.Count > 0 ? mine : (allowed.Count > 0 ? allowed.ToList() : new List<int> { -1 }); // -1 matches nothing
            }
            return requested.Count > 0 ? requested : null;
        }

        // Sidebar routes a sales executive may open (same list as SALES_EXECUTIVE_ROUTES in WorkNest_FE admin.guard.ts).
        private static readonly HashSet<string> SalesExecutiveRoutes = new(StringComparer.OrdinalIgnoreCase)
        {
            "/admin", "/admin/dashboard", "/admin/kyc", "/admin/quotations", "/admin/agreements", "/admin/lease-templates", "/admin/invoices",
            "/admin/bookings", "/admin/attendants", "/admin/biometric-users", "/admin/machine-users",
            "/admin/contacts", "/admin/complaints", "/admin/whatsapp", "/admin/network", "/admin/network/clients", "/admin/network/devices",
            "/admin/network/internet", "/admin/network/wifi"
        };

        /// <summary>
        /// Admin sidebar badges: counts of items waiting on staff right now, keyed by sidebar route
        /// (e.g. { "/admin/bookings": 2 }). Polled every 60 s by every open admin tab, so it is one cheap query.
        /// Only routes the caller's role can open are returned; machines offline is admin / super admin only.
        /// Super admins see every location; location-bound roles every location they are assigned to where the data has one.
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

            // Every location the user is assigned to (as their lists show), not only the main one; null = all locations.
            var scope = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            // Tour inquiries and complaints: every branch the user is assigned to, plus the ones without a branch.
            var branches = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            var badges = await _dashboard.GetNavBadgesAsync(scope, includeMachines: isAdmin, User.GetEmail(), branches);
            if (!isAdmin)
                badges = badges.Where(kv => SalesExecutiveRoutes.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            else
            {
                // Sales executives' requests to add people to full rooms wait on Attendants & Access until an admin decides.
                var pending = (await _attendants.GetCapacityRequestsAsync("Pending", null, User.IsLocationBoundRole() ? User.GetLocationIds() : null)).Count;
                if (pending > 0)
                {
                    badges = new Dictionary<string, int>(badges);
                    badges["/admin/attendants"] = badges.GetValueOrDefault("/admin/attendants") + pending;
                }
            }
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

            var scope = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            var branches = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            if (!await _dashboard.MarkNavBadgesReadAsync(scope, email, routes, branches))
                return StatusCode(503, new { isSuccessful = false, message = "Mark as read is not set up yet: the WN_NAV_BadgeReads table is missing." });
            return Ok(new { isSuccessful = true });
        }

        [HttpGet("/")]
        [AllowAnonymous]
        public IActionResult Root() =>
            Ok(new { app = "WorkNest ASP.NET Core API", status = "healthy" });

        /// <summary>
        /// Chart drill-down of the overview: the records behind one bar / slice / point, on exactly the overview's rules (so
        /// the totals are the chart's figure). kind = revenue-month (month) | aging-bucket (bucket) | top-customer (customer;
        /// period, or from + to) | new-bookings (month) | leases-ending (month) | space-type (spaceType) | occupied-month
        /// (month). month = yyyy-MM; from / to = yyyy-MM-dd (inclusive, both together; they replace the period). Same roles
        /// and location rules as the overview. Bad parameters: 400. Before the database update DASH_02: 503 with a message.
        /// Read-only (stored procedures dbo.WN_Dash_OverviewInvoices / Bookings / Spaces).
        /// </summary>
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]
        [HttpGet("api/dashboard/overview-details/{kind}")]
        public async Task<IActionResult> OverviewDetails(string kind, [FromQuery] int? locationId, [FromQuery] string? locationIds,
            [FromQuery] WorkNest.Application.DTOs.Dashboard.DashboardOverviewDetailsQuery query)
        {
            var result = await _dashboard.GetOverviewDetailsAsync(kind, Scope(locationId, locationIds), query);
            if (result.IsSuccessful) return Ok(result);
            return result.Message == DashboardOverviewDetails.DbUpdateMessage
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, result)
                : BadRequest(result);
        }
    }
}
