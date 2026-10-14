using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// The roles as stored in dbo.WN_Roles, for the Users page role picker (names shown come from DisplayName).
    /// Super admins and admins only. Before the database update SM_01 has run, 503.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
    public class RoleController : ControllerBase
    {
        private readonly IRoleService _roles;

        public RoleController(IRoleService roles)
        {
            _roles = roles;
        }

        /// <summary>Active roles [{ id, roleName, displayName }] ordered by Id.</summary>
        [HttpGet("api/roles")]
        public async Task<IActionResult> List()
        {
            try
            {
                return Ok(await _roles.GetRolesAsync());
            }
            catch (RolesDatabaseUpdateMissingException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(ex.Message));
            }
        }
    }
}
