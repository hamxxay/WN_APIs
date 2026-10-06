using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.SpaceConfig;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

using WorkNest.API.Extensions;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    [ValidateLocationScope]
    public class SpaceConfigController : ControllerBase
    {
        private readonly ISpaceConfigService _config;
        private readonly IDbRepository _db;
        public SpaceConfigController(ISpaceConfigService config, IDbRepository db) { _config = config; _db = db; }

        // Staff = admin / super admin / sales executive / receptionist; customers (role "general") are not staff.
        private const string StaffRoles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive";
        private const string AdminRoles = "admin,Admin,super_admin,SuperAdmin";

        [HttpGet("api/space-config")]
        [AllowAnonymous]
        public async Task<IActionResult> Get() =>
            Ok(await _config.GetSpaceConfigAsync());

        [HttpGet("api/space-config/deposit/{category}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetDeposit(string category) =>
            Ok(await _config.GetSecurityDepositAsync(category));

        [HttpPut("api/space-config/{category}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Update(
            string category,
            [FromBody] SpaceConfigUpdateRequest? body) =>
            Ok(await _config.UpdateSpaceConfigAsync(category, body ?? new SpaceConfigUpdateRequest(), User.GetEmail()));

        [HttpPost("api/space-config/generate-inventory")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> GenerateInventory([FromBody] SpaceInventoryRequest request) =>
            StatusCode(201, await _config.GenerateInventoryAsync(request));

        // ── V2 multi-location config endpoints ───────────────────────────────
        // These alias to the V1 config until a full V2 DB layer is implemented.

        [HttpGet("api/space-config/v2")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> GetV2(
            [FromQuery] int? companyId,
            [FromQuery] int? branchId,
            [FromQuery] int? locationId)
        {
            if (User?.Identity?.IsAuthenticated == true && User.IsLocationBoundRole())
            {
                var claimLocId = User.GetLocationId();
                if (claimLocId.HasValue)
                {
                    locationId = claimLocId.Value;
                }
            }
            return Ok(await _config.GetSpaceConfigV2Async(companyId, branchId, locationId));
        }

        [HttpPost("api/space-config/v2")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> CreateV2([FromBody] SpaceConfigV2Request? body) =>
            StatusCode(201, await _config.CreateSpaceConfigV2Async(body ?? new SpaceConfigV2Request(), User.GetEmail()));

        [HttpPut("api/space-config/v2/{id:int}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> UpdateV2(int id, [FromBody] SpaceConfigV2Request? body) =>
            Ok(await _config.UpdateSpaceConfigV2Async(id, body ?? new SpaceConfigV2Request(), User.GetEmail()));

        [HttpDelete("api/space-config/v2/{id:int}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> DeleteV2(int id) =>
            Ok(await _config.DeleteSpaceConfigV2Async(id));

        [HttpPost("api/space-config/v2/{id:int}/generate")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> GenerateV2(int id)
        {
            var result = await _db.GenerateSpaceInventoryAsync(id, 0, string.Empty, 0, 0);
            return StatusCode(201, ApiResponse.Ok(result, "Spaces generated."));
        }

        [HttpGet("api/space-config/v2/{id:int}/spaces")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> GetSpacesV2(int id)
        {
            var spaces = (await _db.GetSpaceStatusForConfigAsync(id)).ToList();
            return Ok(new { isSuccessful = true, data = spaces, total = spaces.Count });
        }

        [HttpPost("api/space-config/v2/delete-spaces")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> DeleteSpacesV2([FromBody] DeleteSpacesFromConfigRequest? request) =>
            Ok(await _config.DeleteSpacesFromConfigAsync(request ?? new DeleteSpacesFromConfigRequest()));
    }
}
