using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Gallery;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class GalleryController : ControllerBase
    {
        private readonly IGalleryService _gallery;
        private readonly IDbRepository _db;
        public GalleryController(IGalleryService gallery, IDbRepository db) { _gallery = gallery; _db = db; }

        private const string AdminRoles = "admin,Admin,super_admin,SuperAdmin";

        [HttpGet("api/gallery/all")]
        [AllowAnonymous]
        public async Task<IActionResult> All([FromQuery] int? locationId) =>
            Ok(ApiResponse.Ok(await _gallery.GetAllImagesAsync(locationId)));

        [HttpGet("api/gallery")]
        [AllowAnonymous]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] int? locationId = null)
        {
            var (items, total) = await _gallery.GetImagesAsync(page, limit, locationId);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpPost("api/gallery")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Create([FromBody] GalleryUpsertRequest request) =>
            StatusCode(201, await _gallery.CreateImageAsync(request, null));

        [HttpPut("api/gallery/{id:int}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Update(int id, [FromBody] GalleryUpdateRequest request) =>
            Ok(await _gallery.UpdateImageAsync(id, request));

        [HttpPut("api/gallery/{publicId:guid}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> UpdateByGuid(Guid publicId, [FromBody] GalleryUpdateRequest request)
        {
            var (rows, _) = await _db.GetGalleryImagesAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Gallery image not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _gallery.UpdateImageAsync(id, request));
        }

        [HttpDelete("api/gallery/{id:int}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _gallery.DeleteImageAsync(id));

        [HttpDelete("api/gallery/{publicId:guid}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> DeleteByGuid(Guid publicId)
        {
            var (rows, _) = await _db.GetGalleryImagesAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Gallery image not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _gallery.DeleteImageAsync(id));
        }
    }
}
