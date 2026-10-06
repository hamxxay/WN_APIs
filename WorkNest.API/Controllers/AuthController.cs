using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Auth;
using WorkNest.Application.Interfaces;
using System.Security.Claims;
using WorkNest.API.Security;

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
            if (!string.IsNullOrWhiteSpace(idToken))
            {
                var (email, error) = await _firebase.VerifyAsync(idToken, HttpContext.RequestAborted);
                if (email == null)
                    return (null, Unauthorized(new { isSuccessful = false, message = error }));
                if (!string.IsNullOrWhiteSpace(claimedEmail) && !string.Equals(claimedEmail.Trim(), email, StringComparison.OrdinalIgnoreCase))
                    return (null, Unauthorized(new { isSuccessful = false, message = "The sign-in token does not match this email." }));
                return (email, null);
            }

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (_config.GetValue<bool>("Firebase:RequireIdToken"))
            {
                _logger.LogWarning("Rejected {Endpoint} without a Firebase token for {Email} from {Ip}", endpoint, claimedEmail, ip);
                return (null, Unauthorized(new { isSuccessful = false, message = "Please update the app and sign in again." }));
            }
            _logger.LogWarning("LEGACY SIGN-IN without a Firebase token: {Endpoint} for {Email} from {Ip} (allowed while Firebase:RequireIdToken is false)", endpoint, claimedEmail, ip);
            return (claimedEmail, null);
        }

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

        [HttpPost("api/auth/login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] UserLoginRequest request)
        {
            var (email, reject) = await VerifySignInAsync(request.FirebaseIdToken, request.Email, "login");
            if (reject != null) return reject;
            request.Email = email!;
            request.Password = null;
            return Ok(await _auth.LoginAsync(request));
        }

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

        [HttpPost("api/auth/logout")]
        [AllowAnonymous]
        public IActionResult Logout() => Ok(_auth.Logout());
    }
}