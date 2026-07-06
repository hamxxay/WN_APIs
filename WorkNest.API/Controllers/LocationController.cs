using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Location;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class LocationController : ControllerBase
    {
        private readonly ILocationService _locations;
        public LocationController(ILocationService locations) => _locations = locations;

        [HttpGet("api/location/all")]
        [AllowAnonymous]
        public async Task<IActionResult> All()
        {
            var items = await _locations.GetAllLocationsAsync();
            return Ok(ApiResponse.Ok(items));
        }

        [HttpGet("api/location")]
        [AllowAnonymous]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string search = "")
        {
            var all = await _locations.GetAllLocationsAsync();
            var (items, total) = PaginationHelper.Paginate(all, page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpPost("api/location")]
        public async Task<IActionResult> Create([FromBody] LocationUpsertRequest request) =>
            StatusCode(201, await _locations.CreateLocationAsync(request));

        [HttpPut("api/location/{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] LocationUpsertRequest request) =>
            Ok(await _locations.UpdateLocationAsync(id, request));

        [HttpDelete("api/location/{id}")]
        public async Task<IActionResult> Delete(string id) =>
            Ok(await _locations.DeleteLocationAsync(id));
    }
}
