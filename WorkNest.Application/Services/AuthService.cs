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

        public AuthService(IDbRepository db, IJwtService jwt)
        {
            _db  = db;
            _jwt = jwt;
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
                guid = row.TryGetValue("idGUID", out var g) ? g?.ToString() : null;
                role = Roles.MapRole(row.TryGetValue("roleId", out var r) ? (int?)Convert.ToInt32(r) : null);
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
                    guid = row.TryGetValue("idGUID", out var g) ? g?.ToString() : null;
                    role = Roles.MapRole(row.TryGetValue("roleId", out var r) ? (int?)Convert.ToInt32(r) : null);
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
                return ApiResponse.Fail(ex.Message);
            }
        }

        /// <summary>Returns the current user profile from the x-user-email header.</summary>
        public async Task<ApiResponse> GetMeAsync(string email)
        {
            var row = await _db.GetUserByEmailAsync(email);
            if (row is null)
                return ApiResponse.Fail("User not found");

            return ApiResponse.Ok(new
            {
                id    = row.TryGetValue("idGUID", out var g) ? g?.ToString() : null,
                email = row.TryGetValue("email", out var e) ? e?.ToString() ?? "" : "",
                name  = row.TryGetValue("name", out var n) ? n?.ToString() ?? "" : "",
                phone = row.TryGetValue("phoneNumber", out var p) ? p?.ToString() ?? "" : "",
                role  = Roles.MapRole(row.TryGetValue("roleId", out var r) ? (int?)Convert.ToInt32(r) : null),
            });
        }

        /// <summary>Stateless logout — JWT is discarded client-side.</summary>
        public ApiResponse Logout() =>
            ApiResponse.Ok("Logged out successfully.");
    }
}
