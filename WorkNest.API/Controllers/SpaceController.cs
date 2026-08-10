using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Space;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class SpaceController : ControllerBase
    {
        private readonly ISpaceService _spaces;
        private readonly IDbRepository _db;
        public SpaceController(ISpaceService spaces, IDbRepository db) { _spaces = spaces; _db = db; }

        [HttpGet("api/space/available")]
        [AllowAnonymous]
        public async Task<IActionResult> Available()
        {
            var items = await _spaces.GetAvailableSpacesAsync();
            return Ok(ApiResponse.Ok(items));
        }

        [HttpGet("api/space/available-grouped")]
        [AllowAnonymous]
        public async Task<IActionResult> AvailableGrouped()
        {
            var items = (await _spaces.GetAvailableSpacesAsync())
                .Cast<IDictionary<string, object?>>()
                .GroupBy(s => s.TryGetValue("CategoryCode", out var c) ? c?.ToString() ?? "Other" : "Other")
                .Select(g => new
                {
                    categoryCode = g.Key,
                    categoryLabel = g.First().TryGetValue("CategoryLabel", out var l) ? l?.ToString() : g.Key,
                    spaces = g.ToList()
                });
            return Ok(ApiResponse.Ok(items));
        }

        [HttpGet("api/space/available-by-type")]
        [AllowAnonymous]
        public async Task<IActionResult> AvailableByType(
            [FromQuery] int spaceTypeId,
            [FromQuery] DateTime startOn,
            [FromQuery] DateTime endOn) =>
            Ok(await _spaces.GetAvailableSpacesByTypeAsync(spaceTypeId, startOn, endOn));

        [HttpGet("api/space/availability-counts")]
        [AllowAnonymous]
        public async Task<IActionResult> AvailabilityCounts() =>
            Ok(await _spaces.GetAvailabilityCountsAsync());

        [HttpGet("api/space/vacant")]
        public async Task<IActionResult> Vacant()
        {
            var items = await _spaces.GetAvailableSpacesAsync();
            return Ok(ApiResponse.Ok(items));
        }

        [HttpGet("api/space")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null)
        {
            var (items, total) = await _spaces.GetSpacesAsync(page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpGet("api/space/{id:int}/summary")]
        public async Task<IActionResult> Summary(int id)
        {
            var result = await _spaces.GetSpaceSummaryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("api/space/{publicId:guid}/summary")]
        public async Task<IActionResult> SummaryByGuid(Guid publicId)
        {
            var (rows, _) = await _db.GetSpacesAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Space not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            var result = await _spaces.GetSpaceSummaryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/space")]
        public async Task<IActionResult> Create([FromBody] SpaceInsertRequest request) =>
            StatusCode(201, await _spaces.CreateSpaceAsync(request, null));

        [HttpPut("api/space/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SpaceUpdateRequest request)
        {
            var result = await _spaces.UpdateSpaceAsync(id, request, null);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPut("api/space/{publicId:guid}")]
        public async Task<IActionResult> UpdateByGuid(Guid publicId, [FromBody] SpaceUpdateRequest request)
        {
            var (rows, _) = await _db.GetSpacesAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Space not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            var result = await _spaces.UpdateSpaceAsync(id, request, null);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpDelete("api/space/{id:int}")]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _spaces.DeleteSpaceAsync(id));

        [HttpDelete("api/space/{publicId:guid}")]
        public async Task<IActionResult> DeleteByGuid(Guid publicId)
        {
            var (rows, _) = await _db.GetSpacesAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Space not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _spaces.DeleteSpaceAsync(id));
        }
    }
}
