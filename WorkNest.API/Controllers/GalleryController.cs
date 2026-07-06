using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Gallery;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class GalleryController : ControllerBase
    {
        private readonly IGalleryService _gallery;
        public GalleryController(IGalleryService gallery) => _gallery = gallery;

        [HttpGet("api/gallery/all")]
        [AllowAnonymous]
        public async Task<IActionResult> All()
        {
            var items = await _gallery.GetAllImagesAsync();
            return Ok(ApiResponse.Ok(items));
        }

        [HttpGet("api/gallery")]
        [AllowAnonymous]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string search = "")
        {
            var all = await _gallery.GetAllImagesAsync();
            var (items, total) = PaginationHelper.Paginate(all, page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpPost("api/gallery")]
        public async Task<IActionResult> Create([FromBody] GalleryUpsertRequest request) =>
            StatusCode(201, await _gallery.CreateImageAsync(request));

        [HttpPut("api/gallery/{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] GalleryUpsertRequest request) =>
            Ok(await _gallery.UpdateImageAsync(id, request));

        [HttpDelete("api/gallery/{id}")]
        public async Task<IActionResult> Delete(string id) =>
            Ok(await _gallery.DeleteImageAsync(id));
    }
}
