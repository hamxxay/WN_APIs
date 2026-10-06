using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkNest.Application.DTOs.Attendant;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>Door access for logged-in app users (customers' staff added as access users in Sales).</summary>
    [ApiController]
    [Authorize]
    public class MobileAccessController : ControllerBase
    {
        private readonly IAttendantService _attendants;

        public MobileAccessController(IAttendantService attendants)
        {
            _attendants = attendants;
        }

        /// <summary>
        /// Checks the entered name, email, CNIC (and phone, if given) against booking access users.
        /// Rate limited like login, since it answers "does this CNIC + email exist".
        /// </summary>
        [HttpPost("api/mobile/access/verify")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Verify([FromBody] MobileAccessVerifyRequest request)
        {
            var result = await _attendants.VerifyMobileAccessAsync(request ?? new MobileAccessVerifyRequest());
            return Ok(ApiResponse.Ok(result, result.Message));
        }
    }
}
