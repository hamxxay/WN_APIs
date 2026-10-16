using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.Application.DTOs.SpaceType;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Space types. Each location has its own (WN_SpaceTypes.LocationId, SpaceTypes_Location P1): the lists are public
    /// and can be narrowed to one location (?locationId=); rows carry LocationId / LocationName. Admins tied to locations
    /// add, change and remove only the types of their own locations (403 otherwise); a super admin any. A rule of the
    /// procedures (a name that already exists at the location, ...) answers 400 with its message.
    /// </summary>
    [ApiController]
    [Authorize]
    public class SpaceTypeController : ControllerBase
    {
        private readonly ISpaceTypeService _spaceTypes;
        public SpaceTypeController(ISpaceTypeService spaceTypes) { _spaceTypes = spaceTypes; }

        private const string AdminRoles = "admin,Admin,super_admin,SuperAdmin";
        private const string NotYourLocation = "You can only manage the space types of your own locations.";
        private const string NoLocation = "Your account isn't assigned to a location, so you can't add space types.";

        /// <summary>Space categories (Id, Code, Label) for the Space Types form: every type needs one (space lists, pricing).</summary>
        [HttpGet("api/spacetype/categories")]
        public async Task<IActionResult> Categories() =>
            Ok(ApiResponse.Ok(await _spaceTypes.GetSpaceCategoriesAsync()));

        [HttpGet("api/spacetype/all")]
        [AllowAnonymous]
        public async Task<IActionResult> All([FromQuery] int? locationId = null) =>
            Ok(ApiResponse.Ok(await _spaceTypes.GetAllSpaceTypesAsync(locationId)));

        [HttpGet("api/spacetype")]
        [AllowAnonymous]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] int? locationId = null)
        {
            var (items, total) = await _spaceTypes.GetSpaceTypesAsync(page, limit, locationId);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        // A missing LocationId: an admin with one location gets that one; an admin with several has to choose; a super
        // admin leaves it empty (the procedure puts the type at location 1, I-8).
        [HttpPost("api/spacetype")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Create([FromBody] SpaceTypeUpsertRequest request)
        {
            request.LocationId = request.LocationId is > 0 ? request.LocationId : null;
            if (User.IsLocationBoundRole())
            {
                var mine = User.GetLocationIds();
                if (mine.Count == 0) return Refused(NoLocation);
                if (request.LocationId is null)
                {
                    if (mine.Count > 1) return BadRequest(ApiResponse.Fail("Choose the location of this space type."));
                    request.LocationId = mine[0];
                }
                else if (!mine.Contains(request.LocationId.Value)) return Refused(NotYourLocation);
            }
            var result = await _spaceTypes.CreateSpaceTypeAsync(request, null);
            return result.IsSuccessful ? StatusCode(201, result) : BadRequest(result);
        }

        // A missing LocationId keeps the type where it is.
        [HttpPut("api/spacetype/{id:int}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Update(int id, [FromBody] SpaceTypeUpsertRequest request)
        {
            IDictionary<string, object?>? row = null;
            if (User.IsLocationBoundRole())   // the type's current location decides (a super admin: as before)
            {
                row = await _spaceTypes.FindSpaceTypeAsync(id, null);
                if (row is null) return NotFound(ApiResponse.Fail("Space type not found"));
            }
            return await UpdateAsync(id, row, request);
        }

        [HttpPut("api/spacetype/{publicId:guid}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> UpdateByGuid(Guid publicId, [FromBody] SpaceTypeUpsertRequest request)
        {
            var row = await _spaceTypes.FindSpaceTypeAsync(null, publicId);
            if (row is null) return NotFound(ApiResponse.Fail("Space type not found"));
            return await UpdateAsync(SpaceTypeService.IdOf(row), row, request);
        }

        [HttpDelete("api/spacetype/{id:int}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Delete(int id)
        {
            IDictionary<string, object?>? row = null;
            if (User.IsLocationBoundRole())
            {
                row = await _spaceTypes.FindSpaceTypeAsync(id, null);
                if (row is null) return NotFound(ApiResponse.Fail("Space type not found"));
            }
            return await DeleteAsync(id, row);
        }

        [HttpDelete("api/spacetype/{publicId:guid}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> DeleteByGuid(Guid publicId)
        {
            var row = await _spaceTypes.FindSpaceTypeAsync(null, publicId);
            if (row is null) return NotFound(ApiResponse.Fail("Space type not found"));
            return await DeleteAsync(SpaceTypeService.IdOf(row), row);
        }

        private async Task<IActionResult> UpdateAsync(int id, IDictionary<string, object?>? row, SpaceTypeUpsertRequest request)
        {
            request.LocationId = request.LocationId is > 0 ? request.LocationId : null;
            var refusal = OutsideMyLocations(row, request.LocationId);
            if (refusal is not null) return refusal;
            var result = await _spaceTypes.UpdateSpaceTypeAsync(id, request, null);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        private async Task<IActionResult> DeleteAsync(int id, IDictionary<string, object?>? row)
        {
            var refusal = OutsideMyLocations(row, null);
            if (refusal is not null) return refusal;
            var result = await _spaceTypes.DeleteSpaceTypeAsync(id);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Location-bound admins: the type's current location and the requested one must be theirs. A type read before P1
        /// has no location and stays editable, as before. A super admin: any.
        /// </summary>
        private IActionResult? OutsideMyLocations(IDictionary<string, object?>? row, int? requested)
        {
            if (!User.IsLocationBoundRole()) return null;
            var mine = User.GetLocationIds();
            var current = row is null ? null : SpaceTypeService.LocationOf(row);
            if (current is not null && !mine.Contains(current.Value)) return Refused(NotYourLocation);
            if (requested is not null && !mine.Contains(requested.Value)) return Refused(NotYourLocation);
            return null;
        }

        private ObjectResult Refused(string message) => StatusCode(403, ApiResponse.Fail(message));
    }
}
