using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.User;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// User management service.
    /// Mirrors all Python user endpoints in main.py exactly.
    /// </summary>
    public class UserService : IUserService
    {
        private readonly IDbRepository _db;
        private readonly ILogger<UserService> _logger;

        public UserService(IDbRepository db, ILogger<UserService> logger)
        {
            _db     = db;
            _logger = logger;
        }

        public async Task<IEnumerable<object>> GetAllUsersAsync()
        {
            var rows = await _db.GetAllUsersAsync();
            var list  = rows.ToList();
            return list.Select(row => (object)new
            {
                id        = row.TryGetValue("IdGuid", out var g)  ? g?.ToString()  : row.TryGetValue("IdGUID", out var g2) ? g2?.ToString() : null,
                idGuid    = row.TryGetValue("IdGuid", out var g3) ? g3?.ToString() : row.TryGetValue("IdGUID", out var g4) ? g4?.ToString() : null,
                email     = row.TryGetValue("Email",  out var e)  ? e?.ToString() ?? "" : "",
                name      = row.TryGetValue("Name",   out var n)  ? n?.ToString() ?? "" : "",
                phone     = row.TryGetValue("Phone",  out var p)  ? p?.ToString() ?? "" :
                            row.TryGetValue("PhoneNumber", out var p2) ? p2?.ToString() ?? "" : "",
                createdAt = row.TryGetValue("CreatedAt", out var c)  ? DateHelper.ToIso(c as DateTime?) :
                            row.TryGetValue("CreatedOn",  out var c2) ? DateHelper.ToIso(c2 as DateTime?) : null,
                isActive  = row.TryGetValue("Status", out var a) ? Convert.ToInt32(a) == 1 : true,
                role      = Roles.FromRow(row),
            });
        }

        /// <summary>
        /// Gets a user by GUID, email, or numeric ID.
        /// Mirrors Python get_user() identifier resolution logic exactly.
        /// </summary>
        public async Task<ApiResponse> GetUserByIdAsync(string id)
        {
            var row = await _db.GetUserByIdAsync(id);
            if (row is null) return ApiResponse.Fail("User not found");

            return ApiResponse.Ok(new
            {
                id        = row.TryGetValue("IdGUID", out var g) ? g?.ToString() : null,
                idGuid    = row.TryGetValue("IdGUID", out var g2) ? g2?.ToString() : null,
                email     = row.TryGetValue("Email", out var e) ? e?.ToString() ?? "" : "",
                name      = row.TryGetValue("Name", out var n) ? n?.ToString() ?? "" : "",
                phone     = row.TryGetValue("PhoneNumber", out var p) ? p?.ToString() ?? "" : "",
                isActive  = row.TryGetValue("Status", out var a2) && Convert.ToInt32(a2) == 1,
                createdAt = DateHelper.ToIso(row.TryGetValue("CreatedOn", out var c) ? c as DateTime? : null),
                role      = Roles.FromRow(row),
            });
        }

        /// <summary>Returns booking and payment history for a user.</summary>
        public async Task<ApiResponse> GetUserHistoryAsync(string id)
        {
            var userRow = await _db.GetUserByIdAsync(id);
            if (userRow is null) return ApiResponse.Fail("User not found");

            if (!userRow.TryGetValue("IdGUID", out var rawGuid) || rawGuid is null)
                return ApiResponse.Fail("User record missing GUID");

            var userGuid = rawGuid.ToString()!;
            var bookings = (await _db.GetBookingsByUserGuidAsync(userGuid)).ToList();
            var payments = (await _db.GetPaymentsByUserGuidAsync(userGuid)).ToList();

            var totalPaid = payments
                .Where(p => p.TryGetValue("paymentStatus", out var s) && s?.ToString() == "Paid")
                .Sum(p => p.TryGetValue("amount", out var a) ? Convert.ToDouble(a) : 0);

            return ApiResponse.Ok(new
            {
                stats = new
                {
                    totalBookings     = bookings.Count,
                    totalPayments     = payments.Count,
                    totalPaidAmount   = totalPaid,
                    failedPayments    = payments.Count(p => p.TryGetValue("paymentStatus", out var s) && s?.ToString() == "Failed"),
                    cancelledBookings = bookings.Count(b => b.TryGetValue("bookingStatus", out var s) && s?.ToString() == "Cancelled"),
                },
                recentBookings = bookings,
                recentPayments = payments,
            });
        }

        public async Task<ApiResponse> CreateUserAsync(UserCreateRequest request)
        {
            var (id, guid) = await _db.SyncUserAsync(
                request.Email, request.FirstName ?? "", request.LastName ?? "", null);
            return ApiResponse.Ok(new { id = guid ?? id?.ToString(), email = request.Email },
                "User created successfully.");
        }

        public async Task<ApiResponse> UpdateUserAsync(string id, UserUpdateRequest request)
        {
            var name = $"{request.FirstName ?? ""} {request.LastName ?? ""}".Trim();
            await _db.UpdateUserAsync(id, name, request.Phone);
            return ApiResponse.Ok("User updated.");
        }

        public async Task<ApiResponse> DeleteUserAsync(string id)
        {
            await _db.SoftDeleteUserAsync(id);
            return ApiResponse.Ok("User deleted.");
        }

        public async Task<ApiResponse> ActivateUserAsync(string id)
        {
            await _db.SetUserStatusAsync(id, 1);
            return ApiResponse.Ok("User activated.");
        }

        public async Task<ApiResponse> DeactivateUserAsync(string id)
        {
            await _db.SetUserStatusAsync(id, 0);
            return ApiResponse.Ok("User deactivated.");
        }

        public async Task<ApiResponse> UpdateUserRoleAsync(string id, UserRoleUpdateRequest request)
        {
            var roleInt = Roles.ReverseMap.TryGetValue(request.Role.ToLower(), out var r)
                ? r : Roles.GeneralId;
            await _db.SetUserRoleAsync(id, roleInt);
            return ApiResponse.Ok("Role updated.");
        }
    }
}
