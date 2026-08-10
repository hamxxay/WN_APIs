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

        public async Task<(IEnumerable<object> Items, int Total)> GetUsersAsync(int page, int limit, string? search)
        {
            var (rows, total) = await _db.GetUsersAsync(page, limit, search);
            var items = rows.Select(r => (object)new
            {
                id        = r.TryGetValue("Id",        out var i)  ? i  : null,
                publicId  = r.TryGetValue("PublicId",  out var g)  ? g?.ToString() : null,
                email     = r.TryGetValue("Email",     out var e)  ? e?.ToString() : null,
                name      = r.TryGetValue("Name",      out var n)  ? n?.ToString() : null,
                phone     = r.TryGetValue("PhoneNumber", out var p) ? p?.ToString() : null,
                isActive  = r.TryGetValue("IsActive",  out var a)  ? Convert.ToBoolean(a) : true,
                createdOn = r.TryGetValue("CreatedOn", out var c)  ? c  : null,
                role      = Roles.FromRow(r),
            });
            return (items, total);
        }

        public async Task<ApiResponse> GetUserByIdAsync(int id)
        {
            var row = await _db.GetUserByIdAsync(id);
            if (row is null) return ApiResponse.Fail("User not found");
            return ApiResponse.Ok(new
            {
                id        = row.TryGetValue("Id",          out var i)  ? i  : null,
                publicId  = row.TryGetValue("PublicId",    out var g)  ? g?.ToString() : null,
                email     = row.TryGetValue("Email",       out var e)  ? e?.ToString() : null,
                name      = row.TryGetValue("Name",        out var n)  ? n?.ToString() : null,
                phone     = row.TryGetValue("PhoneNumber", out var p)  ? p?.ToString() : null,
                isActive  = row.TryGetValue("IsActive",    out var a)  ? Convert.ToBoolean(a) : true,
                createdOn = row.TryGetValue("CreatedOn",   out var c)  ? c  : null,
                role      = Roles.FromRow(row),
            });
        }

        public async Task<ApiResponse> GetUserHistoryAsync(int id)
        {
            var rows = (await _db.GetUserHistoryAsync(id)).ToList();
            return ApiResponse.Ok(new { history = rows, total = rows.Count });
        }

        public async Task<ApiResponse> CreateUserAsync(UserCreateRequest request, int? actorId)
        {
            var roleId = string.IsNullOrEmpty(request.Role)
                ? Roles.GeneralId
                : Roles.ReverseMap.TryGetValue(request.Role.ToLower(), out var r) ? r : Roles.GeneralId;

            var (id, publicId) = await _db.CreateUserAsync(
                request.Email, request.Password, request.Name, request.Phone,
                roleId, request.CompanyId, request.CityId,
                request.Address, request.CnicOrPassport, request.AvatarUrl,
                request.Notes, actorId);

            return ApiResponse.Ok(new { id, publicId, email = request.Email }, "User created successfully.");
        }

        public async Task<ApiResponse> UpdateUserAsync(int id, UserUpdateRequest request)
        {
            await _db.UpdateUserAsync(id, request.Name, request.Phone,
                request.CompanyId, request.CityId, request.Address,
                request.CnicOrPassport, request.AvatarUrl, request.Notes);
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
            var roleId = Roles.ReverseMap.TryGetValue(request.Role.ToLower(), out var r) ? r : Roles.GeneralId;
            await _db.SetUserRoleAsync(id, roleId);
            return ApiResponse.Ok("Role updated.");
        }
    }
}
