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

        [HttpGet("/")]
        [AllowAnonymous]
        public IActionResult Root() =>
            Ok(new { app = "WorkNest ASP.NET Core API", status = "healthy" });
    }
}
