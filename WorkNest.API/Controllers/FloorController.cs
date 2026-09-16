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

        [HttpGet("api/floor")]
        public async Task<IActionResult> List([FromQuery] int? locationId)
        {
            if (User?.Identity?.IsAuthenticated == true && User.IsLocationBoundRole())
            {
                var claimLocId = User.GetLocationId();
                if (claimLocId.HasValue)
                {
                    locationId = claimLocId.Value;
                }
            }
            return Ok(await _floors.GetFloorsAsync(locationId));
        }

        [HttpPost("api/floor")]
        public async Task<IActionResult> Create([FromBody] FloorUpsertRequest request) =>
            StatusCode(201, await _floors.CreateFloorAsync(request, null));
    }
}
