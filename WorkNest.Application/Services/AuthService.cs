using System.Globalization;
using WorkNest.Application.Helpers;
using Microsoft.Extensions.Configuration;
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
        private readonly ILoginHistoryService _loginHistory;
        private readonly IRoleService _roles;
        private readonly DateTime _grandfatherBeforeUtc;
        private readonly bool _requireVerifiedEmail;

        /// <summary>Config key: only sign-ins recorded before this UTC instant let an unverified Firebase email through.</summary>
        public const string GrandfatherCutoffKey = "Security:UnverifiedSignInGrandfatherBeforeUtc";
        /// <summary>
        /// Config key: "true" = an email / password Firebase sign-in needs a verified email (or a sign-in before the
        /// grandfather cutoff), refused with EMAIL_NOT_VERIFIED otherwise. Missing / "false" (default since 16 Oct 2026:
        /// the owner asked to remove email verification for now) = every Firebase sign-in is treated as verified; only a
        /// turned-off account is refused. Turning it back on needs no code or app change: the CMS, website and app show
        /// "Verify your email" whenever the API answers EMAIL_NOT_VERIFIED.
        /// </summary>
        public const string RequireVerifiedEmailKey = "Security:RequireVerifiedEmail";
        /// <summary>Default cutoff (QA_01 deployment): 2026-10-16T00:00:00Z.</summary>
        public static readonly DateTime DefaultGrandfatherCutoffUtc = new(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);

        public AuthService(IDbRepository db, IJwtService jwt, ILogger<AuthService> logger, ILoginHistoryService loginHistory,
            IRoleService roles, IConfiguration? configuration = null)
        {
            _db = db;
            _jwt = jwt;
            _logger = logger;
            _loginHistory = loginHistory;
            _roles = roles;
            _grandfatherBeforeUtc = GrandfatherCutoff(configuration?[GrandfatherCutoffKey], logger);
            _requireVerifiedEmail = bool.TryParse(configuration?[RequireVerifiedEmailKey], out var require) && require;
        }

        /// <summary>The configured cutoff as UTC (a value without an offset is read as UTC); the default when missing or unreadable.</summary>
        public static DateTime GrandfatherCutoff(string? configured, ILogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(configured)) return DefaultGrandfatherCutoffUtc;
            if (DateTimeOffset.TryParse(configured.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                return parsed.UtcDateTime;
            logger?.LogWarning("{Key} = '{Value}' is not a date; using {Default:o}", GrandfatherCutoffKey, configured, DefaultGrandfatherCutoffUtc);
            return DefaultGrandfatherCutoffUtc;
        }

        /// <summary>The role's name for people (WN_Roles.DisplayName, e.g. "CEO", "Sales Manager"); null when not readable.</summary>
        private Task<string?> RoleDisplayNameAsync(string role) => _roles.GetDisplayNameAsync(Roles.ParseRoleId(role));

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
            var phoneError = Phone.Validate(request.Phone);
            if (phoneError != null) return ApiResponse.Fail(phoneError);

            var name = ResolveName(request.Name, request.FirstName, request.LastName, request.Email);
            var roleId = request.RoleId ?? Roles.GeneralId;
            var companyId = request.CompanyId;

            var (id, publicId) = await _db.SyncUserAsync(request.Email, name, request.Phone, request.Password, roleId, companyId);
            await EnsureCustomerAsync(request.Email, name, request.Phone, id);
            return ApiResponse.Ok(new { id, publicId, email = request.Email }, "User registered successfully.");
        }

        public async Task<ApiResponse> LoginAsync(UserLoginRequest request, bool firebaseVerified = false)
        {
            var row = await _db.GetUserByEmailAsync(request.Email);
            string? publicId;
            string role;
            int? locationId = null;

            var name = ResolveName(request.Name, request.FirstName, request.LastName, request.Email);
            // Set by AuthController only after the Firebase ID token verified. A token merely being present proves nothing
            // (any text used to skip the password check).
            bool isFirebaseVerified = firebaseVerified;

            int? newUserId = null;
            if (row is null)
            {
                // WN_Users_GetByEmail only returns active rows: a turned-off account is told so (403 ACCOUNT_DISABLED), but
                // without Firebase proof only when the password matches, so the answer doesn't reveal the account to anyone.
                UserSignInStatus? status = null;
                try
                {
                    status = await _db.GetUserSignInStatusAsync(request.Email);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not read the account status for {Email}", request.Email);
                }

                if (status is { IsActive: false } &&
                    (isFirebaseVerified || PasswordMatches(request.Password, status.PasswordHash)))
                {
                    await RecordQuietly("FAILED", status.Id, request.Email, "Inactive");
                    return Refused(SignInRefusal.AccountDisabled());
                }

                if (!isFirebaseVerified)
                {
                    await RecordQuietly("FAILED", status?.Id, request.Email, status is { IsActive: false } ? "Inactive" : "UnknownUser");
                    return ApiResponse.Fail("Invalid email or password.");
                }
                var (newId, pid) = await _db.SyncUserAsync(request.Email, name, null, request.Password, Roles.GeneralId, request.CompanyId);
                publicId = pid;
                role = Roles.General;
                newUserId = newId;
                await UpdateProfileQuietly(request.Email, () => EnsureCustomerAsync(request.Email, name, null, newId));
            }
            else
            {
                var userId = row.TryGetValue("Id", out var uid) && uid is not null ? Convert.ToInt32(uid) : (int?)null;

                // Check lockout if present
                if (row.TryGetValue("LockoutEnd", out var lo) && lo != null && lo != DBNull.Value)
                {
                    if (DateTimeOffset.TryParse(lo.ToString(), out var lockoutEnd) && lockoutEnd > DateTimeOffset.UtcNow)
                    {
                        try
                        {
                            await _loginHistory.Record("FAILED", userId, request.Email, "LockedOut");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to record locked out login for {Email}", request.Email);
                        }
                        return ApiResponse.Fail("Account is locked out.");
                    }
                }

                if (!isFirebaseVerified)
                {
                    var dbPassword = row.TryGetValue("PasswordHash", out var pw) ? pw?.ToString() : null;
                    // Without Firebase proof only a password matching the stored one signs in. An account with no stored
                    // password (created through Firebase) can't be entered this way; it used to accept any password.
                    if (!PasswordMatches(request.Password, dbPassword))
                    {
                        try
                        {
                            await _loginHistory.Record("FAILED", userId, request.Email, "WrongPassword");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to record wrong password login for {Email}", request.Email);
                        }
                        return ApiResponse.Fail("Invalid email or password.");
                    }
                }

                publicId = (row.TryGetValue("IdGUID", out var idg) && idg is not null && !string.IsNullOrWhiteSpace(idg.ToString())) 
                    ? idg.ToString() 
                    : (row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);
                role = Roles.FromRow(row);
                locationId = row.TryGetValue("LocationId", out var loc) && loc is not null ? Convert.ToInt32(loc) : (int?)null;

                var existingName = row.TryGetValue("Name", out var n) ? n?.ToString() : null;
                var nameToUse = !string.IsNullOrWhiteSpace(existingName) ? existingName : name;

                await UpdateProfileQuietly(request.Email, async () =>
                {
                    await _db.SyncUserAsync(request.Email, nameToUse, null, request.Password, null, request.CompanyId);
                    await EnsureCustomerAsync(request.Email, nameToUse, null, userId);
                });
            }

            var token = _jwt.GenerateToken(publicId ?? "", request.Email, role, locationId,
                row is not null && Roles.IsLocationBoundRole(role) ? await AllowedLocationsAsync(row) : null);

            var resolvedUserId = (row != null && row.TryGetValue("Id", out var rUid) && rUid is not null)
                ? Convert.ToInt32(rUid) 
                : newUserId;

            // Always recorded (also for the CMS's password sign-in). Only sign-ins before the grandfather cutoff let later
            // unverified Firebase sign-ins through (CheckFirebaseSignInAsync).
            await RecordQuietly("LOGIN", resolvedUserId, request.Email, null);

            return ApiResponse.Ok(new { id = publicId, email = request.Email, role, roles = new[] { role },
                roleDisplayName = await RoleDisplayNameAsync(role), locationId, token }, "Login successful.");
        }

        /// <summary>
        /// May this verified Firebase token sign in as its email? Null = yes, else why not (sent as a 403).
        /// With Security:RequireVerifiedEmail off (the default) every token counts as email_verified (rules below).
        /// - email_verified (Google sign-ins, or the owner opened Firebase's verification link): yes, unless the WN_Users
        ///   row is turned off (ACCOUNT_DISABLED).
        /// - not verified: anyone can create a Firebase email / password account for an address that has none yet (e.g.
        ///   staff added in the CMS who never signed in, or a customer without a user row), so only an existing active
        ///   account that signed in BEFORE the cutoff (Security:UnverifiedSignInGrandfatherBeforeUtc, default
        ///   2026-10-16T00:00:00Z; WN_LoginHistory) may continue: it already had its Firebase account then, and Firebase
        ///   refuses a second one for the same email. Later sign-ins don't count: a password sign-in to the CMS
        ///   (api/auth/login) proves nothing about who owns the Firebase account, which an attacker may have created
        ///   first. Everything else, brand-new emails included, gets EMAIL_NOT_VERIFIED and nothing is created until the
        ///   email is verified (the CMS keeps working through its password fallback).
        /// </summary>
        public async Task<SignInRefusal?> CheckFirebaseSignInAsync(string email, bool emailVerified)
        {
            if (!_requireVerifiedEmail) emailVerified = true;
            UserSignInStatus? status;
            try
            {
                status = await _db.GetUserSignInStatusAsync(email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not read the account status for {Email}", email);
                return emailVerified ? null : SignInRefusal.EmailNotVerified(email);
            }

            SignInRefusal? refusal;
            if (emailVerified)
            {
                refusal = status is { IsActive: false } ? SignInRefusal.AccountDisabled() : null;
            }
            else if (status is null || !await SignedInBeforeAsync(status.Id, email))
            {
                refusal = SignInRefusal.EmailNotVerified(email);
            }
            else
            {
                refusal = status.IsActive ? null : SignInRefusal.AccountDisabled();
            }

            if (refusal != null)
                await RecordQuietly("FAILED", status?.Id, email,
                    refusal.Code == SignInRefusal.AccountDisabledCode ? "Inactive" : "EmailNotVerified");
            return refusal;
        }

        private async Task<bool> SignedInBeforeAsync(int userId, string email)
        {
            try
            {
                return await _loginHistory.HasSignedInBeforeAsync(userId, email, _grandfatherBeforeUtc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not read the login history for {Email}", email);
                return false;
            }
        }

        /// <summary>A failed AuthService result carrying data.code, which AuthController sends as a 403.</summary>
        private static ApiResponse Refused(SignInRefusal refusal) => new()
        {
            IsSuccessful = false,
            Message = refusal.Message,
            Data = new SignInRefusalData { Code = refusal.Code }
        };

        private static bool PasswordMatches(string? given, string? stored) =>
            !string.IsNullOrWhiteSpace(given) && !string.IsNullOrWhiteSpace(stored) &&
            string.Equals(stored, given, StringComparison.Ordinal);

        private async Task RecordQuietly(string eventType, int? userId, string email, string? reason)
        {
            try
            {
                await _loginHistory.Record(eventType, userId, email, reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record {EventType} for {Email}", eventType, email);
            }
        }

        /// <summary>
        /// Updates the user / customer rows of an accepted sign-in. A database error there is logged, not a failed sign-in
        /// (a customer-profile error used to turn a correct password into a 500); the next sign-in tries again.
        /// </summary>
        private async Task UpdateProfileQuietly(string email, Func<Task> update)
        {
            try
            {
                await update();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update the profile on sign-in for {Email}", email);
            }
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

                // Silent token refreshes (app start, hourly Firebase refresh) send interactive = false: they get a token
                // but don't add a login-history row or rewrite the user / customer rows every time.
                var interactive = request.IsInteractive;
                int? resolvedUserId;

                if (row is not null)
                {
                    publicId = (row.TryGetValue("IdGUID", out var idg) && idg is not null && !string.IsNullOrWhiteSpace(idg.ToString())) 
                        ? idg.ToString() 
                        : (row.TryGetValue("PublicId", out var g) ? g?.ToString() : null);
                    role = Roles.FromRow(row);
                    locationId = row.TryGetValue("LocationId", out var loc) && loc is not null ? Convert.ToInt32(loc) : (int?)null;

                    var userId = row.TryGetValue("Id", out var uid) && uid is not null ? Convert.ToInt32(uid) : (int?)null;
                    resolvedUserId = userId;
                    if (interactive)
                    {
                        var existingName = row.TryGetValue("Name", out var n) ? n?.ToString() : null;
                        var nameToUse = !string.IsNullOrWhiteSpace(existingName) ? existingName : name;

                        await _db.SyncUserAsync(request.Email, nameToUse, null, null, null, request.CompanyId);
                        await EnsureCustomerAsync(request.Email, nameToUse, null, userId);
                    }
                }
                else
                {
                    // WN_Users_GetByEmail only returns active rows. A turned-off account used to fall through to an insert that
                    // hit the unique email index ("Google login failed", HTTP 200); it is now refused as ACCOUNT_DISABLED.
                    var status = await _db.GetUserSignInStatusAsync(request.Email);
                    if (status is { IsActive: false })
                    {
                        await RecordQuietly("FAILED", status.Id, request.Email, "Inactive");
                        return Refused(SignInRefusal.AccountDisabled());
                    }

                    // A first sign-in is always set up and recorded, whatever `interactive` says.
                    interactive = true;
                    var (newId, pid) = await _db.SyncUserAsync(request.Email, name, null, null, Roles.GeneralId, request.CompanyId);
                    publicId = pid;
                    role = Roles.General;
                    resolvedUserId = newId;
                    await EnsureCustomerAsync(request.Email, name, null, newId);
                }

                var token = _jwt.GenerateToken(publicId ?? "", request.Email, role, locationId,
                row is not null && Roles.IsLocationBoundRole(role) ? await AllowedLocationsAsync(row) : null);

                if (interactive)
                    await RecordQuietly("LOGIN", resolvedUserId, request.Email, null);

                return ApiResponse.Ok(new { id = publicId, email = request.Email, role, roles = new[] { role },
                    roleDisplayName = await RoleDisplayNameAsync(role), locationId, token }, "Google login successful.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google login failed for {Email}", request.Email);
                try
                {
                    await _loginHistory.Record("FAILED", null, request.Email, "GoogleLoginFailed");
                }
                catch { }
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
                roleDisplayName = await RoleDisplayNameAsync(role),
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
            var phoneError = Phone.Validate(phone);
            if (phoneError != null) return ApiResponse.Fail(phoneError);
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
