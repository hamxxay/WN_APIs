using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Auth;
using WorkNest.Application.Interfaces;
using System.Security.Claims;
using WorkNest.API.Security;
using Microsoft.AspNetCore.RateLimiting;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _auth;
        private readonly FirebaseTokenVerifier _firebase;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService auth, FirebaseTokenVerifier firebase, IConfiguration config, ILogger<AuthController> logger)
        {
            _auth = auth;
            _firebase = firebase;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Every sign-in must prove the person really signed in with Firebase. With a token: it is verified
        /// and the email comes from the token (a request can't claim someone else's email). Without one: refused
        /// when Firebase:RequireIdToken is true; until then (old mobile app versions) allowed but logged.
        /// </summary>
        private async Task<(string? Email, IActionResult? Reject)> VerifySignInAsync(string? idToken, string? claimedEmail, string endpoint)
        {
            var requireToken = _config.GetValue<bool>("Firebase:RequireIdToken");

            // If Firebase token verification is not required (RequireIdToken == false), allow sign-in without blocking
            if (!requireToken)
            {
                if (!string.IsNullOrWhiteSpace(idToken) && _firebase.IsConfigured)
                {
                    try
                    {
                        var (verifiedEmail, _) = await _firebase.VerifyAsync(idToken, HttpContext.RequestAborted);
                        if (!string.IsNullOrWhiteSpace(verifiedEmail))
                        {
                            return (verifiedEmail, null);
                        }
                    }
                    catch
                    {
                        // Ignore verification failure when not required
                    }
                }
                return (claimedEmail, null);
            }

            // Strict mode (RequireIdToken == true):
            if (!string.IsNullOrWhiteSpace(idToken))
            {
                var (email, error) = await _firebase.VerifyAsync(idToken, HttpContext.RequestAborted);
                if (email != null)
                {
                    if (!string.IsNullOrWhiteSpace(claimedEmail) && !string.Equals(claimedEmail.Trim(), email, StringComparison.OrdinalIgnoreCase))
                        return (null, Unauthorized(new { isSuccessful = false, message = "The sign-in token does not match this email." }));
                    return (email, null);
                }

                _logger.LogWarning("Rejected {Endpoint} with invalid Firebase token for {Email}: {Error}", endpoint, claimedEmail, error);
                return (null, Unauthorized(new { isSuccessful = false, message = error ?? "Sign-in could not be verified." }));
            }

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            _logger.LogWarning("Rejected {Endpoint} without a Firebase token for {Email} from {Ip}", endpoint, claimedEmail, ip);
            return (null, Unauthorized(new { isSuccessful = false, message = "Please update the app and sign in again." }));
        }

        [EnableRateLimiting("auth")]
        [HttpPost("api/auth/sync")]
        [AllowAnonymous]
        public async Task<IActionResult> Sync([FromBody] UserSyncRequest request)
        {
            var (email, reject) = await VerifySignInAsync(request.FirebaseIdToken, request.Email, "sync");
            if (reject != null) return reject;
            request.Email = email!;
            request.RoleId = null;        // a sign-in can never choose its own role (was: anyone could create a super admin)
            request.CompanyId = null;
            request.PasswordHash = null;  // Firebase owns passwords; never store them here
            return Ok(await _auth.SyncUserAsync(request));
        }

        [EnableRateLimiting("auth")]
        [HttpPost("api/auth/register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] UserRegisterRequest request)
        {
            var (email, reject) = await VerifySignInAsync(request.FirebaseIdToken, request.Email, "register");
            if (reject != null) return reject;
            request.Email = email!;
            request.Password = null;      // Firebase owns passwords; this used to be saved in plain text
            request.RoleId = null;        // self-registration is always a customer (anyone could register as super admin)
            request.CompanyId = null;
            return Ok(await _auth.RegisterAsync(request));
        }

        [EnableRateLimiting("auth")]
        [HttpPost("api/auth/login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] UserLoginRequest request)
        {
            var (email, reject) = await VerifySignInAsync(request.FirebaseIdToken, request.Email, "login");
            if (reject != null) return reject;
            request.Email = email!;
            // Retain request.Password for credential verification
            var authResult = await _auth.LoginAsync(request);
            if (!authResult.IsSuccessful) return Unauthorized(authResult);
            return Ok(authResult);
        }

        [EnableRateLimiting("auth")]
        [HttpPost("api/auth/google-login")]
        [AllowAnonymous]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
        {
            var (email, reject) = await VerifySignInAsync(request.FirebaseIdToken, request.Email, "google-login");
            if (reject != null) return reject;
            request.Email = email!;
            return Ok(await _auth.GoogleLoginAsync(request));
        }

        [HttpGet("api/auth/me")]
        public async Task<IActionResult> Me()
        {
            // Only the signed-in user's own profile (it used to accept any email in an x-user-email header, without a login).
            var email = User.FindFirst(ClaimTypes.Email)?.Value
                        ?? User.FindFirst("email")?.Value
                        ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value;

            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "Sign in required." });

            var result = await _auth.GetMeAsync(email);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPut("api/auth/me")]
        public async Task<IActionResult> UpdateMe([FromBody] UpdateMyProfileRequest request)
        {
            // Only the signed-in user's own profile, identified from the JWT.
            var email = User.FindFirst(ClaimTypes.Email)?.Value
                        ?? User.FindFirst("email")?.Value
                        ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value;

            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "Sign in required." });

            var result = await _auth.UpdateMeAsync(email, request ?? new UpdateMyProfileRequest());
            if (!result.IsSuccessful) return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("api/auth/logout")]
        [AllowAnonymous]
        public IActionResult Logout() => Ok(_auth.Logout());
    }
}
