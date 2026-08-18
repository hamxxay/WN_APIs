using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Auth;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly IDbRepository _db;
        private readonly IJwtService _jwt;
        private readonly ILogger<AuthService> _logger;

        public AuthService(IDbRepository db, IJwtService jwt, ILogger<AuthService> logger)
        {
            _db = db;
            _jwt = jwt;
            _logger = logger;
        }

        public async Task<ApiResponse> SyncUserAsync(UserSyncRequest request)
        {
            var (id, publicId) = await _db.SyncUserAsync(request.Email, request.Name, request.Phone, request.PasswordHash);
            return ApiResponse.Ok(new { id, publicId, email = request.Email }, "User synchronized successfully.");
        }

        public async Task<ApiResponse> RegisterAsync(UserRegisterRequest request)
        {
            var (id, publicId) = await _db.SyncUserAsync(request.Email, request.Name, request.Phone, request.Password);
            await EnsureCustomerAsync(request.Email, request.Name, request.Phone, id);
            return ApiResponse.Ok(new { id, publicId, email = request.Email }, "User registered successfully.");
        }

        public async Task<ApiResponse> LoginAsync(UserLoginRequest request)
        {
            var row = await _db.GetUserByEmailAsync(request.Email);
            string? publicId;
            string role;

            if (row is null)
            {
                var (_, pid) = await _db.SyncUserAsync(request.Email, null, null, request.Password);
                publicId = pid;
                role = Roles.General;
            }
            else
            {
                publicId = row.TryGetValue("PublicId", out var g) ? g?.ToString() : null;
                role = Roles.FromRow(row);
            }

            var token = _jwt.GenerateToken(publicId ?? "", request.Email, role);
            return ApiResponse.Ok(new { id = publicId, email = request.Email, roles = new[] { role }, token }, "Login successful.");
        }

        public async Task<ApiResponse> GoogleLoginAsync(GoogleLoginRequest request)
        {
            try
            {
                var row = await _db.GetUserByEmailAsync(request.Email);
                string? publicId;
                string role;

                if (row is not null)
                {
                    publicId = row.TryGetValue("PublicId", out var g) ? g?.ToString() : null;
                    role = Roles.FromRow(row);
                }
                else
                {
                    var (newId, pid) = await _db.SyncUserAsync(request.Email, request.Name, null);
                    publicId = pid;
                    role = Roles.General;
                    await EnsureCustomerAsync(request.Email, request.Name, null, newId);
                }

                var token = _jwt.GenerateToken(publicId ?? "", request.Email, role);
                return ApiResponse.Ok(new { id = publicId, email = request.Email, roles = new[] { role }, token }, "Google login successful.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google login failed for {Email}", request.Email);
                return ApiResponse.Fail("Google login failed. Please try again.");
            }
        }

        public async Task<ApiResponse> GetMeAsync(string email)
        {
            var row = await _db.GetUserByEmailAsync(email);
            if (row is null) return ApiResponse.Fail("User not found");

            return ApiResponse.Ok(new
            {
                id       = row.TryGetValue("PublicId",    out var g) ? g?.ToString() : null,
                email    = row.TryGetValue("Email",       out var e) ? e?.ToString() : email,
                name     = row.TryGetValue("Name",        out var n) ? n?.ToString() : null,
                phone    = row.TryGetValue("PhoneNumber", out var p) ? p?.ToString() : null,
                role     = Roles.FromRow(row),
            });
        }

        public ApiResponse Logout() => ApiResponse.Ok("Logged out successfully.");

        private async Task EnsureCustomerAsync(string email, string? name, string? phone, int? userId)
        {
            var existing = await _db.GetCustomerByEmailAsync(email);
            if (existing is not null) return;
            var nameParts = (name ?? "").Split(' ', 2);
            await _db.CreateCustomerAsync(
                nameParts[0].Length > 0 ? nameParts[0] : email,
                nameParts.Length > 1 ? nameParts[1] : null,
                email, phone, null, null, null, null, null);
        }
    }
}
