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
            [FromBody] SpaceConfigUpdateRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail) =>
            Ok(await _config.UpdateSpaceConfigAsync(category, request, userEmail));

        [HttpPost("api/space-config/generate-inventory")]
        public async Task<IActionResult> GenerateInventory([FromBody] SpaceInventoryRequest request) =>
            StatusCode(201, await _config.GenerateInventoryAsync(request));
    }
}
