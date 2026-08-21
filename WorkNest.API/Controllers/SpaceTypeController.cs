using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.SpaceType;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class SpaceTypeController : ControllerBase
    {
        private readonly ISpaceTypeService _spaceTypes;
        private readonly IDbRepository _db;
        public SpaceTypeController(ISpaceTypeService spaceTypes, IDbRepository db) { _spaceTypes = spaceTypes; _db = db; }

        [HttpGet("api/spacetype/all")]
        [AllowAnonymous]
        public async Task<IActionResult> All() =>
            Ok(ApiResponse.Ok(await _spaceTypes.GetAllSpaceTypesAsync()));

        [HttpGet("api/spacetype")]
        [AllowAnonymous]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10)
        {
            var (items, total) = await _spaceTypes.GetSpaceTypesAsync(page, limit);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpPost("api/spacetype")]
        public async Task<IActionResult> Create([FromBody] SpaceTypeUpsertRequest request) =>
            StatusCode(201, await _spaceTypes.CreateSpaceTypeAsync(request, null));

        [HttpPut("api/spacetype/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SpaceTypeUpsertRequest request) =>
            Ok(await _spaceTypes.UpdateSpaceTypeAsync(id, request, null));

        [HttpPut("api/spacetype/{publicId:guid}")]
        public async Task<IActionResult> UpdateByGuid(Guid publicId, [FromBody] SpaceTypeUpsertRequest request)
        {
            var (rows, _) = await _db.GetSpaceTypesAsync(1, 10000);
            var match = rows.FirstOrDefault(r => (r.TryGetValue("IdGUID", out var idg) && idg?.ToString() == publicId.ToString()) || (r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString()));
            if (match is null) return NotFound(ApiResponse.Fail("Space type not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _spaceTypes.UpdateSpaceTypeAsync(id, request, null));
        }

        [HttpDelete("api/spacetype/{id:int}")]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _spaceTypes.DeleteSpaceTypeAsync(id));

        [HttpDelete("api/spacetype/{publicId:guid}")]
        public async Task<IActionResult> DeleteByGuid(Guid publicId)
        {
            var (rows, _) = await _db.GetSpaceTypesAsync(1, 10000);
            var match = rows.FirstOrDefault(r => (r.TryGetValue("IdGUID", out var idg) && idg?.ToString() == publicId.ToString()) || (r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString()));
            if (match is null) return NotFound(ApiResponse.Fail("Space type not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _spaceTypes.DeleteSpaceTypeAsync(id));
        }
    }
}
