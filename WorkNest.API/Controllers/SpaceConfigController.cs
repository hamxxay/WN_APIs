using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.SpaceConfig;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class SpaceConfigController : ControllerBase
    {
        private readonly ISpaceConfigService _config;
        public SpaceConfigController(ISpaceConfigService config) => _config = config;

        // ── Legacy ────────────────────────────────────────────
        [HttpGet("api/space-config")]
        public async Task<IActionResult> Get() =>
            Ok(await _config.GetSpaceConfigAsync());

        [HttpGet("api/space-config/deposit/{category}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetDeposit(string category) =>
            Ok(await _config.GetSecurityDepositAsync(category));

        [HttpPut("api/space-config/{category}")]
        public async Task<IActionResult> Update(
            string category,
            [FromBody] SpaceConfigUpdateRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail) =>
            Ok(await _config.UpdateSpaceConfigAsync(category, request, userEmail));

        [HttpPost("api/space-config/generate-inventory")]
        public async Task<IActionResult> GenerateInventory([FromBody] SpaceInventoryRequest request) =>
            StatusCode(201, await _config.GenerateInventoryAsync(request));

        // ── Multi-location ────────────────────────────────────
        [HttpGet("api/space-config/v2")]
        public async Task<IActionResult> GetV2(
            [FromQuery] int? companyId,
            [FromQuery] int? branchId,
            [FromQuery] int? locationId) =>
            Ok(await _config.GetConfigsAsync(companyId, branchId, locationId));

        [HttpPost("api/space-config/v2")]
        public async Task<IActionResult> Create(
            [FromBody] SpaceConfigCreateRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail) =>
            StatusCode(201, await _config.CreateConfigAsync(request, userEmail));

        [HttpPut("api/space-config/v2/{id:int}")]
        public async Task<IActionResult> UpdateV2(
            int id,
            [FromBody] SpaceConfigEditRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail) =>
            Ok(await _config.UpdateConfigAsync(id, request, userEmail));

        [HttpDelete("api/space-config/v2/{id:int}")]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _config.DeleteConfigAsync(id));

        [HttpPost("api/space-config/v2/{id:int}/generate")]
        public async Task<IActionResult> GenerateSpaces(int id) =>
            Ok(await _config.GenerateSpacesAsync(id));

        [HttpGet("api/space-config/v2/{id:int}/spaces")]
        public async Task<IActionResult> GetSpaceStatus(int id) =>
            Ok(await _config.GetSpaceStatusAsync(id));

        [HttpPost("api/space-config/v2/delete-spaces")]
        public async Task<IActionResult> DeleteSpaces([FromBody] DeleteSpacesRequest request) =>
            Ok(await _config.DeleteSpacesAsync(request));
    }
}
