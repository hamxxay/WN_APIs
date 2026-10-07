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
            var name = ResolveName(request.Name, request.FirstName, request.LastName, request.Email);
            var roleId = request.RoleId ?? Roles.GeneralId;
            var companyId = request.CompanyId;

            var (id, publicId) = await _db.SyncUserAsync(request.Email, name, request.Phone, request.PasswordHash, roleId, companyId);
            await EnsureCustomerAsync(request.Email, name, request.Phone, id);
            return ApiResponse.Ok(new { id, publicId, email = request.Email }, "User synchronized successfully.");
        }

        public async Task<ApiResponse> RegisterAsync(UserRegisterRequest request)
        {
            var name = ResolveName(request.Name, request.FirstName, request.LastName, request.Email);
            var roleId = request.RoleId ?? Roles.GeneralId;
            var companyId = request.CompanyId;

            var (id, publicId) = await _db.SyncUserAsync(request.Email, name, request.Phone, request.Password, roleId, companyId);
            await EnsureCustomerAsync(request.Email, name, request.Phone, id);
            return ApiResponse.Ok(new { id, publicId, email = request.Email }, "User registered successfully.");
        }

        public async Task<ApiResponse> LoginAsync(UserLoginRequest request)
        {
            var row = await _db.GetUserByEmailAsync(request.Email);
            string? publicId;
            string role;
            int? locationId = null;

            var name = ResolveName(request.Name, request.FirstName, request.LastName, request.Email);

            if (row is null)
            {
                var (newId, pid) = await _db.SyncUserAsync(request.Email, name, null, request.Password, Roles.GeneralId, request.CompanyId);
                publicId = pid;
                role = Roles.General;
                await EnsureCustomerAsync(request.Email, name, null, newId);
            }
            else
            {
                publicId = (row.TryGetValue("IdGUID", out var idg) && idg is not null && !string.IsNullOrWhiteSpace(idg.ToString())) 
                    ? idg.ToString() 
                    : (row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);
                role = Roles.FromRow(row);
                locationId = row.TryGetValue("LocationId", out var loc) && loc is not null ? Convert.ToInt32(loc) : (int?)null;

                var userId = row.TryGetValue("Id", out var uid) && uid is not null ? Convert.ToInt32(uid) : (int?)null;
                var existingName = row.TryGetValue("Name", out var n) ? n?.ToString() : null;
                var nameToUse = !string.IsNullOrWhiteSpace(existingName) ? existingName : name;

                await _db.SyncUserAsync(request.Email, nameToUse, null, request.Password, null, request.CompanyId);
                await EnsureCustomerAsync(request.Email, nameToUse, null, userId);
            }

            var token = _jwt.GenerateToken(publicId ?? "", request.Email, role, locationId,
                row is not null && Roles.IsLocationBoundRole(role) ? await AllowedLocationsAsync(row) : null);
            return ApiResponse.Ok(new { id = publicId, email = request.Email, role, roles = new[] { role }, locationId, token }, "Login successful.");
        }

        public async Task<ApiResponse> GoogleLoginAsync(GoogleLoginRequest request)
        {
            try
            {
                var row = await _db.GetUserByEmailAsync(request.Email);
                string? publicId;
                string role;
                int? locationId = null;

                var name = ResolveName(request.Name, request.FirstName, request.LastName, request.Email);

                if (row is not null)
                {
                    publicId = (row.TryGetValue("IdGUID", out var idg) && idg is not null && !string.IsNullOrWhiteSpace(idg.ToString())) 
                        ? idg.ToString() 
                        : (row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);
                    role = Roles.FromRow(row);
                    locationId = row.TryGetValue("LocationId", out var loc) && loc is not null ? Convert.ToInt32(loc) : (int?)null;

                    var userId = row.TryGetValue("Id", out var uid) && uid is not null ? Convert.ToInt32(uid) : (int?)null;
                    var existingName = row.TryGetValue("Name", out var n) ? n?.ToString() : null;
                    var nameToUse = !string.IsNullOrWhiteSpace(existingName) ? existingName : name;

                    await _db.SyncUserAsync(request.Email, nameToUse, null, null, null, request.CompanyId);
                    await EnsureCustomerAsync(request.Email, nameToUse, null, userId);
                }
                else
                {
                    var (newId, pid) = await _db.SyncUserAsync(request.Email, name, null, null, Roles.GeneralId, request.CompanyId);
                    publicId = pid;
                    role = Roles.General;
                    await EnsureCustomerAsync(request.Email, name, null, newId);
                }

                var token = _jwt.GenerateToken(publicId ?? "", request.Email, role, locationId,
                row is not null && Roles.IsLocationBoundRole(role) ? await AllowedLocationsAsync(row) : null);
                return ApiResponse.Ok(new { id = publicId, email = request.Email, role, roles = new[] { role }, locationId, token }, "Google login successful.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google login failed for {Email}", request.Email);
                return ApiResponse.Fail("Google login failed. Please try again.");
            }
        }

        /// <summary>Every location the user may work in: primary (WN_Users.LocationId) plus WN_UserLocations.</summary>
        private async Task<List<int>> AllowedLocationsAsync(IDictionary<string, object?> row)
        {
            var allowed = new List<int>();
            if (row.TryGetValue("LocationId", out var loc) && loc is not null) allowed.Add(Convert.ToInt32(loc));
            if (row.TryGetValue("Id", out var idObj) && idObj is not null)
                allowed.AddRange((await _db.GetUserLocationIdsAsync(Convert.ToInt32(idObj))).Where(x => !allowed.Contains(x)));
            return allowed;
        }

        public async Task<ApiResponse> GetMeAsync(string email)
        {
            var row = await _db.GetUserByEmailAsync(email);
            if (row is null) return ApiResponse.Fail("User not found");

            var publicId = (row.TryGetValue("IdGUID", out var idg) && idg is not null && !string.IsNullOrWhiteSpace(idg.ToString())) 
                ? idg.ToString() 
                : (row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);

            var role = Roles.FromRow(row);
            var locationId = row.TryGetValue("LocationId", out var loc) && loc is not null ? Convert.ToInt32(loc) : (int?)null;
            var locationIds = Roles.IsLocationBoundRole(role) ? await AllowedLocationsAsync(row) : new List<int>();

            return ApiResponse.Ok(new
            {
                id         = publicId,
                email      = row.TryGetValue("Email",       out var e) ? e?.ToString() : email,
                name       = row.TryGetValue("Name",        out var n) ? n?.ToString() : null,
                phone      = row.TryGetValue("PhoneNumber", out var p) ? p?.ToString() : null,
                role       = role,
                roles      = new[] { role },
                locationId = locationId,
                locationIds = locationIds,
                customerId = row.TryGetValue("CustomerId",  out var c) ? c?.ToString() : null,
            });
        }

        public ApiResponse Logout() => ApiResponse.Ok("Logged out successfully.");

        /// <summary>
        /// Updates the signed-in user's own name and phone only (role, location and the rest are untouched:
        /// WN_Users_Update keeps every NULL parameter as-is). Unlike sync, it never creates a customer record.
        /// </summary>
        public async Task<ApiResponse> UpdateMeAsync(string email, UpdateMyProfileRequest request)
        {
            var name = (request.Name ?? "").Trim();
            var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
            if (name.Length == 0) return ApiResponse.Fail("Name is required.");
            if (name.Length > 200) return ApiResponse.Fail("Name must be 200 characters or fewer.");
            if (phone != null && (phone.Length > 20 || !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+?[0-9 ()-]{7,20}$")))
                return ApiResponse.Fail("Enter a valid phone number.");

            var row = await _db.GetUserByEmailAsync(email);
            if (row is null || !row.TryGetValue("Id", out var idObj) || idObj is null)
                return ApiResponse.Fail("User not found.");

            await _db.UpdateUserAsync(Convert.ToInt32(idObj), name, phone, null, null, null, null, null, null);
            return await GetMeAsync(email);
        }

        private static string ResolveName(string? name, string? firstName, string? lastName, string email)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            var combined = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            if (!string.IsNullOrWhiteSpace(combined))
            {
                return combined;
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                var parts = email.Split('@');
                if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                {
                    return parts[0].Trim();
                }
            }

            return "User";
        }

        private async Task EnsureCustomerAsync(string email, string? name, string? phone, int? userId, string? company = null)
        {
            var resolvedName = string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name.Trim();
            var existing = await _db.GetCustomerByEmailAsync(email);

            if (existing is not null && existing.TryGetValue("Id", out var eid) && eid is not null)
            {
                if (userId.HasValue && (!existing.TryGetValue("UserId", out var existingUid) || existingUid is null || Convert.ToInt32(existingUid) == 0))
                {
                    var guid = existing.TryGetValue("IdGUID", out var g) ? g?.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(guid))
                    {
                        var firstName = existing.TryGetValue("FirstName", out var fn) ? fn?.ToString() : null;
                        var lastName = existing.TryGetValue("LastName", out var ln) ? ln?.ToString() : null;
                        await _db.UpdateCustomerAsync(guid, firstName, lastName, email, phone, null, null, null, null, true, company);
                    }
                }
                return;
            }

            var nameParts = resolvedName.Split(' ', 2);
            var firstNameVal = nameParts[0].Length > 0 ? nameParts[0] : email.Split('@')[0];
            var lastNameVal = nameParts.Length > 1 ? nameParts[1] : null;

            await _db.CreateCustomerAsync(
                firstNameVal,
                lastNameVal,
                email, phone, null, null, null, null, null, userId, company);
        }
    }
}
