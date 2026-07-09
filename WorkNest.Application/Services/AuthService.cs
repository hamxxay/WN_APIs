using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Auth;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Handles all authentication operations.
    /// Mirrors Python auth endpoints in main.py exactly.
    /// Issues JWT tokens on login/register/google-login.
    /// </summary>
    public class AuthService : IAuthService
    {
        private readonly IDbRepository _db;
        private readonly IJwtService _jwt;
        private readonly ILogger<AuthService> _logger;

        public AuthService(IDbRepository db, IJwtService jwt, ILogger<AuthService> logger)
        {
            _db     = db;
            _jwt    = jwt;
            _logger = logger;
        }

        /// <summary>Syncs a user record — creates if not exists, updates if exists.</summary>
        public async Task<ApiResponse> SyncUserAsync(UserSyncRequest request)
        {
            var (id, guid) = await _db.SyncUserAsync(
                request.Email, request.FirstName ?? "", request.LastName ?? "", request.Phone);
            return ApiResponse.Ok(new { id = guid ?? id?.ToString(), email = request.Email },
                "User synchronized successfully.");
        }

        /// <summary>Registers a new user (delegates to sync).</summary>
        public async Task<ApiResponse> RegisterAsync(UserRegisterRequest request)
        {
            var (id, guid) = await _db.SyncUserAsync(
                request.Email, request.FirstName ?? "", request.LastName ?? "", request.Phone);
            return ApiResponse.Ok(new { id = guid ?? id?.ToString(), email = request.Email },
                "User registered successfully.");
        }

        /// <summary>
        /// Logs in a user. Creates the user if not found.
        /// Returns a JWT token along with id, email, roles.
        /// </summary>
        public async Task<ApiResponse> LoginAsync(UserLoginRequest request)
        {
            var row = await _db.GetUserByEmailAsync(request.Email);
            string? guid;
            string role;

            if (row is null)
            {
                var (id, newGuid) = await _db.SyncUserAsync(request.Email, "", "", null);
                guid = newGuid ?? id?.ToString();
                role = Roles.General;
            }
            else
            {
                guid = row.TryGetValue("IdGUID", out var g) ? g?.ToString() : null;
                role = Roles.FromRow(row);
            }

            var token = _jwt.GenerateToken(guid ?? "", request.Email, role);
            return ApiResponse.Ok(new
            {
                id    = guid,
                email = request.Email,
                roles = new[] { role },
                token
            }, "Login successful.");
        }

        /// <summary>
        /// Google OAuth login — finds or creates user, returns JWT.
        /// Mirrors Python google_login_user() — returns 200 even on error to preserve CORS headers.
        /// </summary>
        public async Task<ApiResponse> GoogleLoginAsync(GoogleLoginRequest request)
        {
            try
            {
                var row = await _db.GetUserByEmailAsync(request.Email);
                string? guid;
                string role;

                if (row is not null)
                {
                    guid = row.TryGetValue("IdGUID", out var g) ? g?.ToString() : null;
                    role = Roles.FromRow(row);
                }
                else
                {
                    var (id, newGuid) = await _db.SyncUserAsync(
                        request.Email, request.FirstName ?? "", request.LastName ?? "", null);
                    guid = newGuid ?? id?.ToString();
                    role = Roles.General;
                }

                var token = _jwt.GenerateToken(guid ?? "", request.Email, role);
                return ApiResponse.Ok(new
                {
                    id    = guid,
                    email = request.Email,
                    roles = new[] { role },
                    token
                }, "Google login successful.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google login failed for {Email}", request.Email);
                return ApiResponse.Fail("Google login failed. Please try again.");
            }
        }

        /// <summary>Returns the current user profile from the x-user-email header.</summary>
        public async Task<ApiResponse> GetMeAsync(string email)
        {
            var emailRow = await _db.GetUserByEmailAsync(email);
            if (emailRow is null)
                return ApiResponse.Fail("User not found");

            var guid = emailRow.TryGetValue("IdGUID", out var g) ? g?.ToString() : null;
            var row  = guid is not null
                ? await _db.GetUserByGuidAsync(guid) ?? emailRow
                : emailRow;

            return ApiResponse.Ok(new
            {
                id    = row.TryGetValue("IdGUID", out var g2) ? g2?.ToString() : guid,
                email = row.TryGetValue("Email",       out var e) ? e?.ToString() ?? email : email,
                name  = row.TryGetValue("Name",        out var n) ? n?.ToString() ?? "" : "",
                phone = row.TryGetValue("PhoneNumber",  out var p) ? p?.ToString() ?? "" : "",
                role  = Roles.FromRow(row),
            });
        }

        /// <summary>Stateless logout — JWT is discarded client-side.</summary>
        public ApiResponse Logout() =>
            ApiResponse.Ok("Logged out successfully.");
    }
}
