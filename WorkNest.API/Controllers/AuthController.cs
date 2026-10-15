using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Auth;
using WorkNest.Application.Interfaces;
using System.Security.Claims;
using WorkNest.API.Filters;
using WorkNest.API.Security;
using WorkNest.API.Configurations;
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
        private readonly TokenSessionValidator _sessions;
        private readonly ILoginHistoryService _loginHistory;
        private readonly IDbRepository _db;
        private readonly SignInAccountLimiter _accountLimiter;

        public AuthController(
            IAuthService auth,
            FirebaseTokenVerifier firebase,
            IConfiguration config,
            ILogger<AuthController> logger,
            TokenSessionValidator sessions,
            ILoginHistoryService loginHistory,
            IDbRepository db,
            SignInAccountLimiter accountLimiter)
        {
            _auth = auth;
            _firebase = firebase;
            _config = config;
            _logger = logger;
            _sessions = sessions;
            _loginHistory = loginHistory;
            _db = db;
            _accountLimiter = accountLimiter;
        }

        /// <summary>
        /// Works out who is signing in. A Firebase ID token, when sent, must verify (whatever
        /// Firebase:RequireIdToken says) and the email then comes from the token, so a request can't claim
        /// someone else's email. Without a token: refused when Firebase:RequireIdToken is true; otherwise the
        /// claimed email comes back UNVERIFIED (Verified = false) and only a password sign-in, checked against the
        /// stored password, may use it. Before, an unverifiable or made-up token was ignored and the claimed email
        /// was trusted, so anyone could sign in as any account (including super admin) by knowing its email.
        /// A verified token says who signed in, not that they own the email: every caller then runs
        /// CheckAccountAsync before using it.
        /// </summary>
        private async Task<(string? Email, IActionResult? Reject, FirebaseIdentity? Identity)> VerifySignInAsync(string? idToken, string? claimedEmail, string endpoint)
        {
            if (!string.IsNullOrWhiteSpace(idToken))
            {
                FirebaseIdentity? identity = null;
                string? error = null;
                try
                {
                    (identity, error) = await _firebase.VerifyAsync(idToken, HttpContext.RequestAborted);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                if (identity != null)
                {
                    if (!string.IsNullOrWhiteSpace(claimedEmail) && !string.Equals(claimedEmail.Trim(), identity.Email, StringComparison.OrdinalIgnoreCase))
                        return (null, Unauthorized(new { isSuccessful = false, message = "The sign-in token does not match this email." }), null);
                    return (identity.Email, null, identity);
                }

                _logger.LogWarning("Rejected {Endpoint} with an invalid Firebase token for {Email}: {Error}", endpoint, claimedEmail, error);
                return (null, Unauthorized(new { isSuccessful = false, message = "Sign-in could not be verified. Please sign in again." }), null);
            }

            if (_config.GetValue<bool>("Firebase:RequireIdToken"))
                return (null, FirebaseTokenRequired(endpoint, claimedEmail), null);

            return (claimedEmail, null, null);
        }

        /// <summary>
        /// May this verified Firebase sign-in use its email (AuthService.CheckFirebaseSignInAsync)? Null = yes, else a 403
        /// { isSuccessful: false, message, data: { code: "EMAIL_NOT_VERIFIED" | "ACCOUNT_DISABLED" } }. Without this an
        /// email / password Firebase account created by anyone for an address with no Firebase account yet (staff added
        /// in the CMS who never signed in, customers without a user row) got that account's role and data.
        /// </summary>
        private async Task<IActionResult?> CheckAccountAsync(FirebaseIdentity identity, string endpoint)
        {
            var refusal = await _auth.CheckFirebaseSignInAsync(identity.Email, identity.EmailVerified);
            if (refusal is null) return null;
            _logger.LogWarning("Refused {Endpoint} for {Email} (provider {Provider}, email verified {Verified}): {Code}",
                endpoint, identity.Email, identity.SignInProvider, identity.EmailVerified, refusal.Code);
            return StatusCode(StatusCodes.Status403Forbidden, refusal.ToBody());
        }

        /// <summary>An AuthService result refused with data.code (e.g. a turned-off account) becomes the same 403 body.</summary>
        private IActionResult? RefusedResult(WorkNest.Common.Responses.ApiResponse result)
        {
            var code = SignInRefusal.CodeOf(result);
            return code is null ? null : StatusCode(StatusCodes.Status403Forbidden, new SignInRefusal(code, result.Message).ToBody());
        }

        /// <summary>Refusal for a sign-in that needs a verified Firebase token (Google sign-in, sync, register).</summary>
        private IActionResult FirebaseTokenRequired(string endpoint, string? claimedEmail)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            _logger.LogWarning("Rejected {Endpoint} without a verified Firebase token for {Email} from {Ip}", endpoint, claimedEmail, ip);
            return Unauthorized(new { isSuccessful = false, message = "Please update the app and sign in again." });
        }

        [EnableRateLimiting("auth")]
        [HttpPost("api/auth/sync")]
        [AllowAnonymous]
        public async Task<IActionResult> Sync([FromBody] UserSyncRequest request)
        {
            var (email, reject, identity) = await VerifySignInAsync(request.FirebaseIdToken, request.Email, "sync");
            if (reject != null) return reject;
            if (identity is null) return FirebaseTokenRequired("sync", request.Email);
            if (await CheckAccountAsync(identity, "sync") is { } refused) return refused;
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
            var (email, reject, identity) = await VerifySignInAsync(request.FirebaseIdToken, request.Email, "register");
            if (reject != null) return reject;
            if (identity is null) return FirebaseTokenRequired("register", request.Email);
            // A new email / password account is refused until its email is verified: no WN_Users row is created before.
            if (await CheckAccountAsync(identity, "register") is { } refused) return refused;
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
            var (email, reject, identity) = await VerifySignInAsync(request.FirebaseIdToken, request.Email, "login");
            if (reject != null)
            {
                try
                {
                    await _loginHistory.Record("FAILED", null, request.Email, "InvalidToken");
                }
                catch { }
                return reject;
            }
            // A Firebase token skips the password check, so it must pass the same email-verified / turned-off check as
            // google-login. The CMS answers a 403 here by signing in with the WorkNest password instead (no token), which
            // is recorded below, so its next Firebase sign-ins pass as "signed in before".
            if (identity != null && await CheckAccountAsync(identity, "login") is { } refused) return refused;
            request.Email = email!;
            request.CompanyId = null;     // a sign-in can never move the user to another company
            // Retain request.Password for credential verification
            var authResult = await _auth.LoginAsync(request, firebaseVerified: identity != null);
            if (!authResult.IsSuccessful) return RefusedResult(authResult) ?? Unauthorized(authResult);
            return Ok(authResult);
        }

        /// <summary>
        /// Exchanges a Firebase sign-in (Google or email / password, website / CMS / app) for the API's JWT.
        /// 403 { isSuccessful: false, message, data: { code } } when the email isn't verified (EMAIL_NOT_VERIFIED) or the
        /// account is turned off (ACCOUNT_DISABLED); 429 after RateLimiting:GoogleLogin:PerAccountPermitLimit exchanges
        /// per account and window. interactive: false (silent refresh) returns the token without a login-history row.
        /// </summary>
        [EnableRateLimiting(RateLimitingConfig.GoogleLoginPolicy)]
        [HttpPost("api/auth/google-login")]
        [AllowAnonymous]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
        {
            // App versions before October 2026 send the Firebase ID token as `idToken`; it is verified the same way.
            var idToken = !string.IsNullOrWhiteSpace(request.FirebaseIdToken) ? request.FirebaseIdToken : request.IdToken;
            var (email, reject, identity) = await VerifySignInAsync(idToken, request.Email, "google-login");
            if (reject != null) return reject;
            if (identity is null) return FirebaseTokenRequired("google-login", request.Email);
            // Per account, not per IP: a whole coworking Wi-Fi shares one IP and the app exchanges on every start / refresh.
            if (!_accountLimiter.TryAcquire(identity.Uid ?? identity.Email, out var retryAfter))
            {
                _logger.LogWarning("google-login rate limited for {Email}", identity.Email);
                return StatusCode(StatusCodes.Status429TooManyRequests, RateLimitingConfig.TooManyRequestsBody(Response, retryAfter));
            }
            if (await CheckAccountAsync(identity, "google-login") is { } refused) return refused;
            request.Email = email!;
            request.CompanyId = null;     // a sign-in can never move the user to another company
            var result = await _auth.GoogleLoginAsync(request);
            return RefusedResult(result) ?? Ok(result);
        }

        [HttpGet("api/auth/me")]
        [CeoAllowed] // the CEO's own profile
        [SalesManagerAllowed] // and the Sales Manager's
        [AccessOfficerAllowed] // and the Access Officer's
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
        [CeoAllowed] // own name / phone only (the app's and CMS's profile screen)
        [SalesManagerAllowed]
        [AccessOfficerAllowed]
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

        /// <summary>
        /// Signs out the bearer token sent with this request: it gets a 401 from now until it expires (kept in this
        /// server's memory, see TokenSessionValidator). Still anonymous: without a valid token it just answers OK.
        /// </summary>
        [HttpPost("api/auth/logout")]
        [AllowAnonymous]
        public async Task<IActionResult> Logout()
        {
            try
            {
                var email = User.FindFirst(ClaimTypes.Email)?.Value
                            ?? User.FindFirst("email")?.Value
                            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value;

                int? userId = null;
                var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
                if (int.TryParse(sub, out var parsedId))
                {
                    userId = parsedId;
                }
                else if (!string.IsNullOrWhiteSpace(email))
                {
                    var userRow = await _db.GetUserByEmailAsync(email);
                    if (userRow != null && userRow.TryGetValue("Id", out var uid) && uid != null)
                        userId = Convert.ToInt32(uid);
                }

                await _loginHistory.Record("LOGOUT", userId, email, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record logout event");
            }

            _sessions.RevokeCurrent(HttpContext);
            return Ok(_auth.Logout());
        }
    }
}
