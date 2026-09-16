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
            var items = rows.Select(r => (object)new
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
                locationName = r.TryGetValue("LocationName", out var ln) ? ln?.ToString() : null,
            });
            return (items, total);
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
            int? finalLocationId = request.LocationId;

            if (Roles.IsSuperAdmin(roleId) || roleId == Roles.GeneralId)
            {
                finalLocationId = null;
            }
            else if (Roles.IsLocationBoundRole(roleId))
            {
                if (!finalLocationId.HasValue || finalLocationId.Value <= 0)
                {
                    return ApiResponse.Fail("Location is required for Admin and Sales Executive roles.");
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

            return ApiResponse.Ok(new { id, publicId, email = request.Email, locationId = finalLocationId }, "User created successfully.");
        }

        public async Task<ApiResponse> UpdateUserAsync(int id, UserUpdateRequest request)
        {
            int? finalLocationId = request.LocationId;

            if (!string.IsNullOrWhiteSpace(request.Role))
            {
                int roleId = Roles.ParseRoleId(request.Role, Roles.GeneralId);

                if (Roles.IsSuperAdmin(roleId) || roleId == Roles.GeneralId)
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
            }
            else if (request.LocationId.HasValue)
            {
                var existingUser = await _db.GetUserByIdAsync(id);
                var existingRoleId = existingUser?.TryGetValue("RoleId", out var rid) == true && rid is not null ? Convert.ToInt32(rid) : Roles.GeneralId;
                if (Roles.IsSuperAdmin(existingRoleId) || existingRoleId == Roles.GeneralId)
                {
                    finalLocationId = null;
                }
                await _db.SetUserRoleAndLocationAsync(id, existingRoleId, finalLocationId);
            }

            await _db.UpdateUserAsync(id, request.Name, request.Phone,
                request.CompanyId, request.CityId, request.Address,
                request.CnicOrPassport, request.AvatarUrl, request.Notes, finalLocationId);
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

            if (Roles.IsSuperAdmin(roleId) || roleId == Roles.GeneralId)
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
                        return ApiResponse.Fail("Location is required when updating role to Admin or Sales Executive.");
                    }
                }
            }

            await _db.SetUserRoleAndLocationAsync(id, roleId, finalLocationId);
            return ApiResponse.Ok("Role and location binding updated.");
        }
    }
}
