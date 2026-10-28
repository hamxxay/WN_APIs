using WorkNest.Application.Helpers;
using WorkNest.Application.DTOs.User;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class UserService : IUserService
    {
        private readonly IDbRepository _db;
        private readonly IRoleService? _roles;
        public UserService(IDbRepository db, IRoleService? roles = null)
        {
            _db = db;
            _roles = roles;
        }

        private const string SalesManagerLocationRequired = "A Sales Manager needs at least one location.";
        private const string AccessOfficerLocationRequired = "An Access Officer needs at least one location.";
        private const string NetworkEngineerLocationRequired = "Location is required for Network Engineers.";

        /// <summary>
        /// Roles that only work at their locations and must be given one (nothing is saved without it): a Sales Manager
        /// sees only their locations' figures, an Access Officer only finds people on their locations' bookings, a Network
        /// Engineer only manages their locations' networks. Null for the other roles (their older rules stay as they were).
        /// </summary>
        private static string? LocationRequiredFor(int roleId) =>
            Roles.IsSalesManager(roleId) ? SalesManagerLocationRequired
            : Roles.IsAccessOfficer(roleId) ? AccessOfficerLocationRequired
            : Roles.IsNetworkEngineer(roleId) ? NetworkEngineerLocationRequired
            : null;

        public async Task<(IEnumerable<object> Items, int Total)> GetUsersAsync(int page, int limit, string? search, int? locationId = null)
        {
            var (rows, total) = await _db.GetUsersAsync(page, limit, search, locationId);
            var rowList = rows.ToList();
            var extra = await _db.GetUserLocationsMapAsync(rowList
                .Where(r => r.TryGetValue("Id", out var v) && v is not null).Select(r => Convert.ToInt32(r["Id"])));
            // Role names shown to people come from dbo.WN_Roles.DisplayName (cached), never from code.
            var roleNames = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var role in rowList.Select(Roles.FromRow).Distinct(StringComparer.OrdinalIgnoreCase))
                roleNames[role] = _roles is null ? null : await _roles.GetDisplayNameAsync(Roles.ParseRoleId(role));
            var items = rowList.Select(r => (object)new
            {
                id           = r.TryGetValue("Id",           out var i)  ? i  : null,
                publicId     = r.TryGetValue("PublicId",     out var g)  ? g?.ToString() : null,
                email        = r.TryGetValue("Email",        out var e)  ? e?.ToString() : null,
                name         = r.TryGetValue("Name",         out var n)  ? n?.ToString() : null,
                phone        = r.TryGetValue("PhoneNumber",  out var p)  ? p?.ToString() : null,
                isActive     = r.TryGetValue("IsActive",     out var a)  ? Convert.ToBoolean(a) : true,
                createdOn    = r.TryGetValue("CreatedOn",    out var c)  ? c  : null,
                createdAt    = r.TryGetValue("CreatedOn",    out var ca) ? ca : null,
                role         = Roles.FromRow(r),
                roleDisplayName = roleNames.GetValueOrDefault(Roles.FromRow(r)),
                locationId   = r.TryGetValue("LocationId",   out var loc)&& loc is not null ? Convert.ToInt32(loc) : (int?)null,
                locationIds  = AllLocations(r, extra).Select(x => x.Id).ToList(),
                locationName = string.Join(", ", AllLocations(r, extra).Select(x => x.Name).Where(x => x.Length > 0)),
            });
            return (items, total);
        }

        /// <summary>Primary location (WN_Users) followed by any extra ones from WN_UserLocations, without duplicates.</summary>
        private static List<(int Id, string Name)> AllLocations(IDictionary<string, object?> r, Dictionary<int, List<(int Id, string Name)>> extra)
        {
            var list = new List<(int Id, string Name)>();
            if (r.TryGetValue("LocationId", out var loc) && loc is not null)
                list.Add((Convert.ToInt32(loc), r.TryGetValue("LocationName", out var ln) ? ln?.ToString() ?? "" : ""));
            if (r.TryGetValue("Id", out var idObj) && idObj is not null && extra.TryGetValue(Convert.ToInt32(idObj), out var more))
                list.AddRange(more.Where(m => list.All(x => x.Id != m.Id)));
            return list;
        }

        /// <summary>
        /// Saves an Admin/Sales Executive's locations: the primary goes to WN_Users.LocationId, the full set to
        /// WN_UserLocations. Other roles have none. Returns an error message, or null when saved.
        /// </summary>
        private async Task<string?> SaveLocationsAsync(int userId, int roleId, int? primary, List<int>? locationIds, int? actorId)
        {
            if (!Roles.IsLocationBoundRole(roleId))
            {
                await _db.SetUserLocationsAsync(userId, Array.Empty<int>(), actorId);
                return null;
            }
            var set = (locationIds ?? new List<int>()).Where(x => x > 0).ToList();
            if (primary is > 0 && !set.Contains(primary.Value)) set.Insert(0, primary.Value);
            if (set.Count == 0) return null;
            var saved = await _db.SetUserLocationsAsync(userId, set, actorId);
            if (!saved && set.Count > 1)
                return "Only the first location was saved. Multiple locations need the WN_UserLocations table (script WN_UserLocations.txt).";
            return null;
        }

        /// <summary>Picks the primary location: the given one if it is in the list, otherwise the first of the list.</summary>
        private static int? PrimaryLocation(int? locationId, List<int>? locationIds)
        {
            var list = (locationIds ?? new List<int>()).Where(x => x > 0).ToList();
            if (list.Count == 0) return locationId;
            return locationId is > 0 && list.Contains(locationId.Value) ? locationId : list[0];
        }

        public async Task<ApiResponse> GetUserByIdAsync(int id)
        {
            var row = await _db.GetUserByIdAsync(id);
            if (row is null) return ApiResponse.Fail("User not found");
            return ApiResponse.Ok(new
            {
                id           = row.TryGetValue("Id",           out var i)  ? i  : null,
                publicId     = row.TryGetValue("PublicId",     out var g)  ? g?.ToString() : null,
                email        = row.TryGetValue("Email",        out var e)  ? e?.ToString() : null,
                name         = row.TryGetValue("Name",         out var n)  ? n?.ToString() : null,
                phone        = row.TryGetValue("PhoneNumber",  out var p)  ? p?.ToString() : null,
                isActive     = row.TryGetValue("IsActive",     out var a)  ? Convert.ToBoolean(a) : true,
                createdOn    = row.TryGetValue("CreatedOn",    out var c)  ? c  : null,
                role         = Roles.FromRow(row),
                locationId   = row.TryGetValue("LocationId",   out var loc)&& loc is not null ? Convert.ToInt32(loc) : (int?)null,
                locationName = row.TryGetValue("LocationName", out var ln) ? ln?.ToString() : null,
            });
        }

        public async Task<ApiResponse> GetUserHistoryAsync(int id)
        {
            var rows = (await _db.GetUserHistoryAsync(id)).ToList();
            return ApiResponse.Ok(new { history = rows, total = rows.Count });
        }

        public async Task<ApiResponse> CreateUserAsync(UserCreateRequest request, int? actorId)
        {
            var cnicError = Cnic.Validate(request.CnicOrPassport);
            if (cnicError != null) return ApiResponse.Fail(cnicError);
            var phoneError = Phone.Validate(request.Phone);
            if (phoneError != null) return ApiResponse.Fail(phoneError);

            var roleId = Roles.ParseRoleId(request.Role, Roles.GeneralId);
            int? finalLocationId = PrimaryLocation(request.LocationId, request.LocationIds);

            if (!Roles.IsLocationBoundRole(roleId))
            {
                finalLocationId = null;
            }
            else if (Roles.IsLocationBoundRole(roleId))
            {
                if (!finalLocationId.HasValue || finalLocationId.Value <= 0)
                {
                    return ApiResponse.Fail(LocationRequiredFor(roleId) ?? "Location is required for Sales Executives.");
                }
            }

            // One WorkNest account per email (UX_WN_Users_Email): the insert would fail with a database error ("An unexpected
            // error occurred"), e.g. for a customer who already signed up on the website or app with this email.
            var existing = await _db.GetUserSignInStatusAsync(request.Email);
            if (existing is { IsActive: true })
                return ApiResponse.Fail("This email already has a WorkNest account (for example a customer who signed up). Search for it on the Users page and edit it, e.g. change its role, instead of creating a new user.");
            // Deleting a user only turns the account off (WN_Users_Delete sets IsActive = 0) and the email stays taken, so
            // creating the user again turns that account back on with the new details (19 Oct 2026).
            if (existing is not null)
                return await RestoreDeletedUserAsync(existing.Id, request, roleId, finalLocationId, actorId);

            var (id, publicId) = await _db.CreateUserAsync(
                request.Email, request.Password, request.Name, request.Phone,
                roleId, request.CompanyId, request.CityId,
                request.Address, request.CnicOrPassport, request.AvatarUrl,
                request.Notes, actorId, finalLocationId);

            // The user is saved at this point. The customer profile and the locations are extras: when one of them fails the
            // user still exists, so say what's missing instead of failing the whole request (a retry would only hit the
            // "email already has an account" check).
            string? customerWarning = null;
            try
            {
                var existingCustomer = await _db.GetCustomerByEmailAsync(request.Email);
                if (existingCustomer is null)
                {
                    var nameParts = (request.Name ?? "").Split(' ', 2);
                    await _db.CreateCustomerAsync(
                        nameParts[0],
                        nameParts.Length > 1 ? nameParts[1] : null,
                        request.Email,
                        request.Phone,
                        request.CnicOrPassport,
                        request.Address,
                        request.CityId,
                        request.Notes,
                        null,
                        id);
                }
                else if (id.HasValue && (!existingCustomer.TryGetValue("UserId", out var existingUid) || existingUid is null || Convert.ToInt32(existingUid) == 0))
                {
                    var custGuid = existingCustomer.TryGetValue("IdGUID", out var cg) ? cg?.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(custGuid))
                    {
                        await _db.UpdateCustomerAsync(custGuid, null, null, request.Email, request.Phone, request.CnicOrPassport, request.Address, request.CityId, request.Notes, null);
                    }
                }
            }
            catch (Exception ex)
            {
                customerWarning = $"User created, but their customer profile could not be saved ({ex.Message}).";
            }

            string? locationWarning;
            try
            {
                locationWarning = id.HasValue ? await SaveLocationsAsync(id.Value, roleId, finalLocationId, request.LocationIds, actorId) : null;
            }
            catch (Exception ex)
            {
                locationWarning = $"User created, but their locations could not be saved ({ex.Message}). Edit the user to set them.";
            }
            return ApiResponse.Ok(new { id, publicId, email = request.Email, locationId = finalLocationId },
                locationWarning ?? customerWarning ?? "User created successfully.");
        }

        /// <summary>Users page password rules (Firebase's minimum is 6; WN_Users.PasswordHash holds 500).</summary>
        internal const int MinPasswordLength = 6, MaxPasswordLength = 128;
        internal const string PasswordDbUpdateMessage =
            "Database update needed: run the script PW_01_user_password_TEST (PW_01_user_password_LIVE on the live database). The other changes were saved; the password was not changed.";

        internal const string RestoredMessage = "This email had a deleted account. It has been turned back on with these details.";
        internal const string RestorePasswordDbUpdateMessage =
            "Database update needed: run the script PW_01_user_password_TEST (PW_01_user_password_LIVE on the live database). Nothing was changed; the deleted account is still turned off.";

        /// <summary>
        /// Create for an email whose account was deleted (turned off): the same account is turned back on with the new
        /// details, since one WorkNest account per email (UX_WN_Users_Email) rules out a second row. The password goes
        /// first, so when it can't be saved (PW_01 not run) nothing changes and the account stays off. Fields left empty
        /// keep what the account had (WN_Users_Update leaves a NULL as it is).
        /// </summary>
        private async Task<ApiResponse> RestoreDeletedUserAsync(int id, UserCreateRequest request, int roleId, int? finalLocationId, int? actorId)
        {
            var password = string.IsNullOrWhiteSpace(request.Password) ? null : request.Password;
            if (password != null && (password.Length < MinPasswordLength || password.Length > MaxPasswordLength))
                return ApiResponse.Fail($"The password must be {MinPasswordLength} to {MaxPasswordLength} characters.");
            if (password != null)
            {
                var changed = await _db.SetUserPasswordAsync(id, password);
                if (changed is null) return ApiResponse.Fail(RestorePasswordDbUpdateMessage);
                if (changed == 0) return ApiResponse.Fail("User not found.");
            }

            await _db.SetUserRoleAndLocationAsync(id, roleId, finalLocationId);
            await _db.UpdateUserAsync(id, request.Name, request.Phone, request.CompanyId, request.CityId, request.Address,
                request.CnicOrPassport, request.AvatarUrl, request.Notes, finalLocationId);
            await _db.SetUserStatusAsync(id, true);

            string? warning = null;
            try
            {
                var existingCustomer = await _db.GetCustomerByEmailAsync(request.Email);
                if (existingCustomer is null)
                {
                    var nameParts = (request.Name ?? "").Split(' ', 2);
                    await _db.CreateCustomerAsync(nameParts[0], nameParts.Length > 1 ? nameParts[1] : null, request.Email,
                        request.Phone, request.CnicOrPassport, request.Address, request.CityId, request.Notes, null, id);
                }
                else if (!existingCustomer.TryGetValue("UserId", out var existingUid) || existingUid is null || Convert.ToInt32(existingUid) == 0)
                {
                    var custGuid = existingCustomer.TryGetValue("IdGUID", out var cg) ? cg?.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(custGuid))
                    {
                        await _db.UpdateCustomerAsync(custGuid, null, null, request.Email, request.Phone, request.CnicOrPassport, request.Address, request.CityId, request.Notes, null);
                    }
                }
            }
            catch (Exception ex)
            {
                warning = $"{RestoredMessage} Their customer profile could not be saved ({ex.Message}).";
            }
            try
            {
                var locationWarning = await SaveLocationsAsync(id, roleId, finalLocationId, request.LocationIds, actorId);
                if (locationWarning != null) warning = $"{RestoredMessage} {locationWarning}";
            }
            catch (Exception ex)
            {
                warning = $"{RestoredMessage} Their locations could not be saved ({ex.Message}). Edit the user to set them.";
            }

            var row = await _db.GetUserByIdAsync(id);
            var publicId = row != null && row.TryGetValue("IdGUID", out var g) ? g?.ToString() : null;
            return ApiResponse.Ok(new { id, publicId, email = request.Email, locationId = finalLocationId, restored = true }, warning ?? RestoredMessage);
        }

        public async Task<ApiResponse> UpdateUserAsync(int id, UserUpdateRequest request)
        {
            // Users page, edit: a new password is optional (empty = keep the current one). Checked before anything is saved.
            var newPassword = string.IsNullOrWhiteSpace(request.Password) ? null : request.Password;
            if (newPassword != null && (newPassword.Length < MinPasswordLength || newPassword.Length > MaxPasswordLength))
                return ApiResponse.Fail($"The password must be {MinPasswordLength} to {MaxPasswordLength} characters.");

            string? newEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
            if (newEmail != null)
            {
                if (!newEmail.Contains('@') || !newEmail.Contains('.'))
                    return ApiResponse.Fail("Please enter a valid email address.");

                var existingWithEmail = await _db.CheckUserEmailStatusAsync(newEmail);
                if (existingWithEmail.HasValue && existingWithEmail.Value.Id.HasValue && existingWithEmail.Value.Id.Value != id)
                {
                    return ApiResponse.Fail("This email is already in use by another account.");
                }
            }

            var cnicError = Cnic.Validate(request.CnicOrPassport);
            if (cnicError != null) return ApiResponse.Fail(cnicError);
            var phoneError = Phone.Validate(request.Phone);
            if (phoneError != null) return ApiResponse.Fail(phoneError);

            int? finalLocationId = PrimaryLocation(request.LocationId, request.LocationIds);
            int? effectiveRoleId = null;

            if (!string.IsNullOrWhiteSpace(request.Role))
            {
                int roleId = Roles.ParseRoleId(request.Role, Roles.GeneralId);

                if (!Roles.IsLocationBoundRole(roleId))
                {
                    finalLocationId = null;
                }
                else if (Roles.IsLocationBoundRole(roleId))
                {
                    if (!finalLocationId.HasValue || finalLocationId.Value <= 0)
                    {
                        var existingUser = await _db.GetUserByIdAsync(id);
                        var existingLoc = existingUser?.TryGetValue("LocationId", out var loc) == true && loc is not null ? Convert.ToInt32(loc) : (int?)null;
                        if (existingLoc.HasValue && existingLoc.Value > 0)
                        {
                            finalLocationId = existingLoc.Value;
                        }
                        else if (LocationRequiredFor(roleId) is { } locationRequired)
                        {
                            // A Sales Manager / Access Officer / Network Engineer works only at their locations: one is required (nothing is saved).
                            return ApiResponse.Fail(locationRequired);
                        }
                    }
                }

                await _db.SetUserRoleAndLocationAsync(id, roleId, finalLocationId);
                effectiveRoleId = roleId;
            }
            else if (finalLocationId.HasValue)
            {
                var existingUser = await _db.GetUserByIdAsync(id);
                var existingRoleId = existingUser?.TryGetValue("RoleId", out var rid) == true && rid is not null ? Convert.ToInt32(rid) : Roles.GeneralId;
                if (!Roles.IsLocationBoundRole(existingRoleId))
                {
                    finalLocationId = null;
                }
                await _db.SetUserRoleAndLocationAsync(id, existingRoleId, finalLocationId);
                effectiveRoleId = existingRoleId;
            }

            await _db.UpdateUserAsync(id, request.Name, request.Phone,
                request.CompanyId, request.CityId, request.Address,
                request.CnicOrPassport, request.AvatarUrl, request.Notes, finalLocationId, newEmail);

            // Only touch the location list when the form sent one (or changed the role).
            string? warning = null;
            if (effectiveRoleId.HasValue && (request.LocationIds != null || !Roles.IsLocationBoundRole(effectiveRoleId.Value)))
                warning = await SaveLocationsAsync(id, effectiveRoleId.Value, finalLocationId, request.LocationIds, null);

            // Before 19 Oct 2026 an edit dropped the password without a word, so sign-in kept the old one.
            if (newPassword != null)
            {
                var changed = await _db.SetUserPasswordAsync(id, newPassword);
                if (changed is null) return ApiResponse.Fail(PasswordDbUpdateMessage);
                if (changed == 0) return ApiResponse.Fail("User not found.");
            }

            var done = newPassword != null ? "User updated. Password changed." : "User updated.";
            return ApiResponse.Ok(warning != null ? (newPassword != null ? $"{warning} Password changed." : warning) : done);
        }

        public async Task<ApiResponse> DeleteUserAsync(int id)
        {
            await _db.DeleteUserAsync(id);
            return ApiResponse.Ok("User deleted.");
        }

        public async Task<ApiResponse> ActivateUserAsync(int id)
        {
            await _db.SetUserStatusAsync(id, true);
            return ApiResponse.Ok("User activated.");
        }

        public async Task<ApiResponse> DeactivateUserAsync(int id)
        {
            await _db.SetUserStatusAsync(id, false);
            return ApiResponse.Ok("User deactivated.");
        }

        public async Task<ApiResponse> UpdateUserRoleAsync(int id, UserRoleUpdateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Role))
                return ApiResponse.Fail("Role is required.");

            int roleId = Roles.ParseRoleId(request.Role, Roles.GeneralId);
            int? finalLocationId = request.LocationId;

            if (!Roles.IsLocationBoundRole(roleId))
            {
                finalLocationId = null;
            }
            else if (Roles.IsLocationBoundRole(roleId))
            {
                if (!finalLocationId.HasValue || finalLocationId.Value <= 0)
                {
                    // Check if user already has a location assigned in DB
                    var existingUser = await _db.GetUserByIdAsync(id);
                    var existingLoc = existingUser?.TryGetValue("LocationId", out var loc) == true && loc is not null ? Convert.ToInt32(loc) : (int?)null;
                    if (existingLoc.HasValue && existingLoc.Value > 0)
                    {
                        finalLocationId = existingLoc.Value;
                    }
                    else
                    {
                        return ApiResponse.Fail(LocationRequiredFor(roleId) ?? "Location is required when making a user a Sales Executive.");
                    }
                }
            }

            await _db.SetUserRoleAndLocationAsync(id, roleId, finalLocationId);
            return ApiResponse.Ok("Role and location binding updated.");
        }
    }
}
