using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.SpaceType;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class SpaceTypeController : ControllerBase
    {
        private readonly ISpaceTypeService _spaceTypes;
        public SpaceTypeController(ISpaceTypeService spaceTypes) => _spaceTypes = spaceTypes;

        [HttpGet("api/spacetype/all")]
        [AllowAnonymous]
        public async Task<IActionResult> All()
        {
            var items = await _spaceTypes.GetAllSpaceTypesAsync();
            return Ok(ApiResponse.Ok(items));
        }

        [HttpGet("api/spacetype")]
        [AllowAnonymous]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string search = "")
        {
            var all = await _spaceTypes.GetAllSpaceTypesAsync();
            var (items, total) = PaginationHelper.Paginate(all, page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpPost("api/spacetype")]
        public async Task<IActionResult> Create([FromBody] SpaceTypeUpsertRequest request) =>
            StatusCode(201, await _spaceTypes.CreateSpaceTypeAsync(request));

        [HttpPut("api/spacetype/{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] SpaceTypeUpsertRequest request) =>
            Ok(await _spaceTypes.UpdateSpaceTypeAsync(id, request));

        [HttpDelete("api/spacetype/{id}")]
        public async Task<IActionResult> Delete(string id) =>
            Ok(await _spaceTypes.DeleteSpaceTypeAsync(id));
    }
}
