using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Space;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class SpaceController : ControllerBase
    {
        private readonly ISpaceService _spaces;
        public SpaceController(ISpaceService spaces) => _spaces = spaces;

        [HttpGet("api/space/vacant")]
        public async Task<IActionResult> Vacant()
        {
            var items = await _spaces.GetVacantSpacesAsync();
            return Ok(new { data = items, total = items.Count() });
        }

        [HttpGet("api/space/available")]
        [AllowAnonymous]
        public async Task<IActionResult> Available()
        {
            var items = await _spaces.GetAvailableSpacesAsync();
            var (paged, total) = PaginationHelper.Paginate(items, 1, 1000);
            return Ok(new PaginatedResponse<object> { Data = paged, Total = total });
        }

        [HttpGet("api/space/available-by-type")]
        [AllowAnonymous]
        public async Task<IActionResult> AvailableByType(
            [FromQuery] string spaceType,
            [FromQuery] string? startDateTime,
            [FromQuery] string? endDateTime) =>
            Ok(await _spaces.GetAvailableSpacesByTypeAsync(spaceType, startDateTime, endDateTime));

        [HttpGet("api/space/availability-counts")]
        [AllowAnonymous]
        public async Task<IActionResult> AvailabilityCounts() =>
            Ok(await _spaces.GetAvailabilityCountsAsync());

        [HttpGet("api/space")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string search = "")
        {
            var all = await _spaces.GetAllSpacesAsync();
            var (items, total) = PaginationHelper.Paginate(all, page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpGet("api/space/{id}/summary")]
        public async Task<IActionResult> Summary(string id)
        {
            var result = await _spaces.GetSpaceSummaryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/space")]
        public async Task<IActionResult> Create([FromBody] SpaceInsertRequest request) =>
            StatusCode(201, await _spaces.CreateSpaceAsync(request));

        [HttpPut("api/space/{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] SpaceUpdateRequest request)
        {
            var result = await _spaces.UpdateSpaceAsync(id, request);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpDelete("api/space/{id}")]
        public async Task<IActionResult> Delete(string id) =>
            Ok(await _spaces.DeleteSpaceAsync(id));
    }
}
