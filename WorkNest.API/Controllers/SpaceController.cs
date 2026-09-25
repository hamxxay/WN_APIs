using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Space;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

using WorkNest.API.Extensions;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    [ValidateLocationScope]
    public class SpaceController : ControllerBase
    {
        private readonly ISpaceService _spaces;
        private readonly IDbRepository _db;
        public SpaceController(ISpaceService spaces, IDbRepository db) { _spaces = spaces; _db = db; }

        [HttpGet("api/billing-periods")]
        [AllowAnonymous]
        public async Task<IActionResult> GetBillingPeriods()
        {
            var periods = await _db.GetBillingPeriodsAsync();
            return Ok(ApiResponse.Ok(periods));
        }

        [HttpGet("api/space/available")]
        [AllowAnonymous]
        public async Task<IActionResult> Available([FromQuery] string? shiftType = "24_7")
        {
            var items = await _spaces.GetAvailableSpacesAsync(shiftType);
            return Ok(ApiResponse.Ok(items));
        }

        [HttpGet("api/space/available-grouped")]
        [AllowAnonymous]
        public async Task<IActionResult> AvailableGrouped([FromQuery] string? shiftType = "24_7")
        {
            var items = (await _spaces.GetAvailableSpacesAsync(shiftType))
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
            [FromQuery] DateTime endOn,
            [FromQuery] string? shiftType = "24_7") =>
            Ok(await _spaces.GetAvailableSpacesByTypeAsync(spaceTypeId, startOn, endOn, shiftType));

        [HttpGet("api/space/availability-counts")]
        [AllowAnonymous]
        public async Task<IActionResult> AvailabilityCounts([FromQuery] string? shiftType = "24_7") =>
            Ok(await _spaces.GetAvailabilityCountsAsync(shiftType));

        [HttpGet("api/space/vacant")]
        public async Task<IActionResult> Vacant([FromQuery] string? shiftType = "24_7")
        {
            var items = await _spaces.GetAvailableSpacesAsync(shiftType);
            return Ok(ApiResponse.Ok(items));
        }

        [HttpGet("api/space")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null)
        {
            var (items, total) = await _spaces.GetSpacesAsync(page, limit, search);
            if (User?.Identity?.IsAuthenticated == true && User.IsLocationBoundRole())
            {
                var claimLocId = User.GetLocationId();
                if (claimLocId.HasValue)
                {
                    var filtered = items.Where(s =>
                    {
                        if (s is IDictionary<string, object?> dict && dict.TryGetValue("LocationId", out var loc) && loc != null)
                            return Convert.ToInt32(loc) == claimLocId.Value;
                        var prop = s.GetType().GetProperty("LocationId") ?? s.GetType().GetProperty("locationId");
                        if (prop != null)
                        {
                            var v = prop.GetValue(s);
                            return v != null && Convert.ToInt32(v) == claimLocId.Value;
                        }
                        return true;
                    }).ToList();
                    return Ok(new PaginatedResponse<object> { Data = filtered, Total = filtered.Count });
                }
            }
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
            var match = rows.FirstOrDefault(r => (r.TryGetValue("IdGUID", out var idg) && idg?.ToString() == publicId.ToString()) || (r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString()));
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
            var match = rows.FirstOrDefault(r => (r.TryGetValue("IdGUID", out var idg) && idg?.ToString() == publicId.ToString()) || (r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString()));
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
            var match = rows.FirstOrDefault(r => (r.TryGetValue("IdGUID", out var idg) && idg?.ToString() == publicId.ToString()) || (r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString()));
            if (match is null) return NotFound(ApiResponse.Fail("Space not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _spaces.DeleteSpaceAsync(id));
        }
    }
}
