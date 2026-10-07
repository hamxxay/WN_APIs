using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Floor;
using WorkNest.Application.Interfaces;

using WorkNest.API.Extensions;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    [ValidateLocationScope]
    public class FloorController : ControllerBase
    {
        private readonly IFloorService _floors;
        public FloorController(IFloorService floors) => _floors = floors;

        // Staff = admin / super admin / sales executive / receptionist; customers (role "general") are not staff.
        private const string StaffRoles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive";
        private const string AdminRoles = "admin,Admin,super_admin,SuperAdmin";

        [HttpGet("api/floor")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> List([FromQuery] int? locationId)
        {
            if (User?.Identity?.IsAuthenticated == true && User.IsLocationBoundRole())
            {
                // Any assigned location may be requested; otherwise the primary one.
                var allowedLocIds = User.GetLocationIds();
                if (allowedLocIds.Count > 0 && !(locationId is int req && allowedLocIds.Contains(req)))
                {
                    locationId = allowedLocIds[0];
                }
            }
            return Ok(await _floors.GetFloorsAsync(locationId));
        }

        [HttpPost("api/floor")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Create([FromBody] FloorUpsertRequest request) =>
            StatusCode(201, await _floors.CreateFloorAsync(request, null));
    }
}
