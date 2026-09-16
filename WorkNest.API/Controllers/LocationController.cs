using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Location;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

using WorkNest.API.Extensions;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    [ValidateLocationScope]
    public class LocationController : ControllerBase
    {
        private readonly ILocationService _locations;
        private readonly IDbRepository _db;
        public LocationController(ILocationService locations, IDbRepository db) { _locations = locations; _db = db; }

        [HttpGet("api/location/all")]
        [AllowAnonymous]
        public async Task<IActionResult> All()
        {
            var all = await _locations.GetAllLocationsAsync();
            if (User?.Identity?.IsAuthenticated == true && User.IsLocationBoundRole())
            {
                var claimLocId = User.GetLocationId();
                if (claimLocId.HasValue)
                {
                    var filtered = all.Where(l =>
                    {
                        if (l is System.Collections.IDictionary dict && dict.Contains("Id") && dict["Id"] != null)
                            return Convert.ToInt32(dict["Id"]) == claimLocId.Value;
                        var prop = l.GetType().GetProperty("Id");
                        if (prop != null)
                        {
                            var v = prop.GetValue(l);
                            return v != null && Convert.ToInt32(v) == claimLocId.Value;
                        }
                        return false;
                    });
                    return Ok(ApiResponse.Ok(filtered));
                }
            }
            return Ok(ApiResponse.Ok(all));
        }

        [HttpGet("api/location")]
        [AllowAnonymous]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null)
        {
            var (items, total) = await _locations.GetLocationsAsync(page, limit, search);
            if (User?.Identity?.IsAuthenticated == true && User.IsLocationBoundRole())
            {
                var claimLocId = User.GetLocationId();
                if (claimLocId.HasValue)
                {
                    var filtered = items.Where(l =>
                    {
                        if (l is System.Collections.IDictionary dict && dict.Contains("Id") && dict["Id"] != null)
                            return Convert.ToInt32(dict["Id"]) == claimLocId.Value;
                        var prop = l.GetType().GetProperty("Id");
                        if (prop != null)
                        {
                            var v = prop.GetValue(l);
                            return v != null && Convert.ToInt32(v) == claimLocId.Value;
                        }
                        return false;
                    }).ToList();
                    return Ok(new PaginatedResponse<object> { Data = filtered, Total = filtered.Count });
                }
            }
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpPost("api/location")]
        public async Task<IActionResult> Create([FromBody] LocationUpsertRequest request) =>
            StatusCode(201, await _locations.CreateLocationAsync(request, null));

        [HttpPut("api/location/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] LocationUpdateRequest request) =>
            Ok(await _locations.UpdateLocationAsync(id, request));

        [HttpPut("api/location/{publicId:guid}")]
        public async Task<IActionResult> UpdateByGuid(Guid publicId, [FromBody] LocationUpdateRequest request)
        {
            var (rows, _) = await _db.GetLocationsAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => (r.TryGetValue("IdGUID", out var idg) && idg?.ToString() == publicId.ToString()) || (r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString()));
            if (match is null) return NotFound(ApiResponse.Fail("Location not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _locations.UpdateLocationAsync(id, request));
        }

        [HttpDelete("api/location/{id:int}")]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _locations.DeleteLocationAsync(id));

        [HttpDelete("api/location/{publicId:guid}")]
        public async Task<IActionResult> DeleteByGuid(Guid publicId)
        {
            var (rows, _) = await _db.GetLocationsAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => (r.TryGetValue("IdGUID", out var idg) && idg?.ToString() == publicId.ToString()) || (r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString()));
            if (match is null) return NotFound(ApiResponse.Fail("Location not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _locations.DeleteLocationAsync(id));
        }
    }
}
