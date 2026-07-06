using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Floor;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class FloorController : ControllerBase
    {
        private readonly IFloorService _floors;
        public FloorController(IFloorService floors) => _floors = floors;

        [HttpGet("api/floor")]
        public async Task<IActionResult> List([FromQuery] int? locationId) =>
            Ok(await _floors.GetAllFloorsAsync(locationId));

        [HttpPost("api/floor")]
        public async Task<IActionResult> Create([FromBody] FloorUpsertRequest request) =>
            StatusCode(201, await _floors.CreateFloorAsync(request));
    }
}
