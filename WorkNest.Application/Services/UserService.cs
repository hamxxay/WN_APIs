using WorkNest.Application.DTOs.User;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class UserService : IUserService
    {
        private readonly IDbRepository _db;
        public UserService(IDbRepository db) => _db = db;

        public async Task<(IEnumerable<object> Items, int Total)> GetUsersAsync(int page, int limit, string? search, int? locationId = null)
        {
            var (rows, total) = await _db.GetUsersAsync(page, limit, search, locationId);
            var rowList = rows.ToList();
            var extra = await _db.GetUserLocationsMapAsync(rowList
                .Where(r => r.TryGetValue("Id", out var v) && v is not null).Select(r => Convert.ToInt32(r["Id"])));
            var items = rowList.Select(r => (object)new
            {
                id           = r.TryGetValue("Id",           out var i)  ? i  : null,
                publicId     = r.TryGetValue("PublicId",     out var g)  ? g?.ToString() : null,
                email        = r.TryGetValue("Email",        out var e)  ? e?.ToString() : null,
                name         = r.TryGetValue("Name",         out var n)  ? n?.ToString() : null,
                phone        = r.TryGetValue("PhoneNumber",  out var p)  ? p?.ToString() : null,
                isActive     = r.TryGetValue("IsActive",     out var a)  ? Convert.ToBoolean(a) : true,
                createdOn    = r.TryGetValue("CreatedOn",    out var c)  ? c  : null,
                role         = Roles.FromRow(r),
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
                    return ApiResponse.Fail("Location is required for Sales Executives.");
                }
            }

            var (id, publicId) = await _db.CreateUserAsync(
                request.Email, request.Password, request.Name, request.Phone,
                roleId, request.CompanyId, request.CityId,
                request.Address, request.CnicOrPassport, request.AvatarUrl,
                request.Notes, actorId, finalLocationId);

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
                    null);
            }

            string? locationWarning = id.HasValue ? await SaveLocationsAsync(id.Value, roleId, finalLocationId, request.LocationIds, actorId) : null;
            return ApiResponse.Ok(new { id, publicId, email = request.Email, locationId = finalLocationId },
                locationWarning ?? "User created successfully.");
        }

        public async Task<ApiResponse> UpdateUserAsync(int id, UserUpdateRequest request)
        {
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
                request.CnicOrPassport, request.AvatarUrl, request.Notes, finalLocationId);

            // Only touch the location list when the form sent one (or changed the role).
            if (effectiveRoleId.HasValue && (request.LocationIds != null || !Roles.IsLocationBoundRole(effectiveRoleId.Value)))
            {
                var warning = await SaveLocationsAsync(id, effectiveRoleId.Value, finalLocationId, request.LocationIds, null);
                if (warning != null) return ApiResponse.Ok(warning);
            }
            return ApiResponse.Ok("User updated.");
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
                        return ApiResponse.Fail("Location is required when making a user a Sales Executive.");
                    }
                }
            }

            await _db.SetUserRoleAndLocationAsync(id, roleId, finalLocationId);
            return ApiResponse.Ok("Role and location binding updated.");
        }
    }
}
