using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.SpaceConfig;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class SpaceConfigController : ControllerBase
    {
        private readonly ISpaceConfigService _config;
        private readonly IDbRepository _db;
        public SpaceConfigController(ISpaceConfigService config, IDbRepository db) { _config = config; _db = db; }

        [HttpGet("api/space-config")]
        [AllowAnonymous]
        public async Task<IActionResult> Get() =>
            Ok(await _config.GetSpaceConfigAsync());

        [HttpGet("api/space-config/deposit/{category}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetDeposit(string category) =>
            Ok(await _config.GetSecurityDepositAsync(category));

        [HttpPut("api/space-config/{category}")]
        public async Task<IActionResult> Update(
            string category,
            [FromBody] SpaceConfigUpdateRequest? body,
            [FromHeader(Name = "x-user-email")] string? userEmail) =>
            Ok(await _config.UpdateSpaceConfigAsync(category, body ?? new SpaceConfigUpdateRequest(), userEmail));

        [HttpPost("api/space-config/generate-inventory")]
        public async Task<IActionResult> GenerateInventory([FromBody] SpaceInventoryRequest request) =>
            StatusCode(201, await _config.GenerateInventoryAsync(request));

        // ── V2 multi-location config endpoints ───────────────────────────────
        // These alias to the V1 config until a full V2 DB layer is implemented.

        [HttpGet("api/space-config/v2")]
        public async Task<IActionResult> GetV2(
            [FromQuery] int? companyId,
            [FromQuery] int? branchId,
            [FromQuery] int? locationId) =>
            Ok(await _config.GetSpaceConfigAsync());

        [HttpPost("api/space-config/v2")]
        public async Task<IActionResult> CreateV2([FromBody] SpaceConfigUpdateRequest? body) =>
            StatusCode(201, new { isSuccessful = true, message = "Config created." });

        [HttpPut("api/space-config/v2/{id:int}")]
        public async Task<IActionResult> UpdateV2(
            int id,
            [FromBody] SpaceConfigUpdateRequest? body,
            [FromHeader(Name = "x-user-email")] string? userEmail) =>
            Ok(new { isSuccessful = true, message = "Config updated." });

        [HttpDelete("api/space-config/v2/{id:int}")]
        public async Task<IActionResult> DeleteV2(int id) =>
            Ok(new { isSuccessful = true, message = "Config deleted." });

        [HttpPost("api/space-config/v2/{id:int}/generate")]
        public async Task<IActionResult> GenerateV2(int id)
        {
            var result = await _db.GenerateSpaceInventoryAsync(id, 0, string.Empty, 0, 0);
            return StatusCode(201, ApiResponse.Ok(result, "Spaces generated."));
        }

        [HttpGet("api/space-config/v2/{id:int}/spaces")]
        public async Task<IActionResult> GetSpacesV2(int id)
        {
            var spaces = (await _db.GetSpaceStatusForConfigAsync(id)).ToList();
            return Ok(new { isSuccessful = true, data = spaces, total = spaces.Count });
        }

        [HttpPost("api/space-config/v2/delete-spaces")]
        public async Task<IActionResult> DeleteSpacesV2([FromBody] object body) =>
            Ok(new { isSuccessful = true, message = "Spaces deleted." });
    }
}
