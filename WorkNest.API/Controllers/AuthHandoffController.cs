using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkNest.API.Configurations;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Cross-site sign-in between worknestpk.com ("web") and the CMS ("cms"). The signed-in site asks for a one-time code
    /// and opens the other site's /auth/handoff#code=...; that site redeems it for its own session (same body as
    /// api/auth/login). Codes are single use, valid 60 seconds and for one target. Before the database script
    /// AUTH_HANDOFF_01_login_handoff has run both answer 503 and the sites fall back to the other site's sign-in page.
    /// </summary>
    [ApiController]
    [Authorize]
    public class AuthHandoffController : ControllerBase
    {
        public sealed class HandoffRequest
        {
            /// <summary>"cms" (staff only) or "web".</summary>
            public string? Target { get; set; }
        }

        public sealed class RedeemRequest
        {
            public string? Code { get; set; }
            /// <summary>The site redeeming the code: must be the target it was made for.</summary>
            public string? Target { get; set; }
        }

        private readonly IAuthHandoffService _handoff;

        public AuthHandoffController(IAuthHandoffService handoff)
        {
            _handoff = handoff;
        }

        /// <summary>A one-time code for the signed-in user: 200 data { code, expiresInSeconds, target }.</summary>
        [HttpPost("api/auth/handoff")]
        [CeoAllowed] // every staff role opens the CMS from worknestpk.com
        [SalesManagerAllowed]
        [AccessOfficerAllowed]
        [NetworkEngineerAllowed]
        public async Task<IActionResult> Create([FromBody] HandoffRequest? request)
        {
            var email = User.GetEmail();
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(ApiResponse.Fail("Sign in required."));
            try
            {
                var result = await _handoff.CreateAsync(email, request?.Target, HttpContext.RequestAborted);
                return StatusCode(result.StatusCode, result.Response);
            }
            catch (AuthHandoffMissingException)
            {
                return NotInstalled();
            }
        }

        /// <summary>
        /// Spends the code and signs its user in (200, the api/auth/login body); 401 when it is unknown, used, expired or
        /// for the other site, 403 when the account is turned off or not staff (CMS).
        /// </summary>
        [EnableRateLimiting(RateLimitingConfig.AuthPolicy)]
        [HttpPost("api/auth/handoff/redeem")]
        [AllowAnonymous]
        public async Task<IActionResult> Redeem([FromBody] RedeemRequest? request)
        {
            try
            {
                var result = await _handoff.RedeemAsync(request?.Code, request?.Target, HttpContext.RequestAborted);
                return StatusCode(result.StatusCode, result.Response);
            }
            catch (AuthHandoffMissingException)
            {
                return NotInstalled();
            }
        }

        private IActionResult NotInstalled() =>
            StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(AuthHandoffMissingException.UserMessage));
    }
}
