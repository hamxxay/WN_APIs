using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Auth;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _auth;
        public AuthController(IAuthService auth) => _auth = auth;

        [HttpPost("api/auth/sync")]
        [AllowAnonymous]
        public async Task<IActionResult> Sync([FromBody] UserSyncRequest request) =>
            Ok(await _auth.SyncUserAsync(request));

        [HttpPost("api/auth/register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] UserRegisterRequest request) =>
            Ok(await _auth.RegisterAsync(request));

        [HttpPost("api/auth/login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] UserLoginRequest request) =>
            Ok(await _auth.LoginAsync(request));

        [HttpPost("api/auth/google-login")]
        [AllowAnonymous]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request) =>
            Ok(await _auth.GoogleLoginAsync(request));

        [HttpGet("api/auth/me")]
        public async Task<IActionResult> Me([FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "User email header required" });
            var result = await _auth.GetMeAsync(userEmail);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/auth/logout")]
        public IActionResult Logout() => Ok(_auth.Logout());
    }
}
