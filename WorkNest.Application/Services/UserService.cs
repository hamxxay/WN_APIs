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

        public UserService(IDbRepository db) => _db = db;

        public async Task<IEnumerable<object>> GetAllUsersAsync()
        {
            var rows = await _db.GetAllUsersAsync();
            return rows.Select(row => (object)new
            {
                id        = row.TryGetValue("idGuid", out var g) ? g?.ToString() : null,
                idGuid    = row.TryGetValue("idGuid", out var g2) ? g2?.ToString() : null,
                email     = row.TryGetValue("email", out var e) ? e?.ToString() ?? "" : "",
                name      = row.TryGetValue("name", out var n) ? n?.ToString() ?? "" : "",
                phone     = row.TryGetValue("phone", out var p) ? p?.ToString() ?? "" : "",
                createdAt = row.TryGetValue("createdAt", out var c) ? DateHelper.ToIso(c as DateTime?) : null,
                isActive  = row.TryGetValue("isActive", out var a) && Convert.ToInt32(a) == 1,
                role      = Roles.MapRole(row.TryGetValue("roleId", out var r) ? (int?)Convert.ToInt32(r) : null),
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
                id        = row.TryGetValue("idGUID", out var g) ? g?.ToString() : null,
                email     = row.TryGetValue("email", out var e) ? e?.ToString() ?? "" : "",
                firstName = row.TryGetValue("name", out var n) ? n?.ToString() ?? "" : "",
                phone     = row.TryGetValue("phoneNumber", out var p) ? p?.ToString() ?? "" : "",
                isActive  = true,
                createdAt = DateHelper.ToIso(row.TryGetValue("createdOn", out var c) ? c as DateTime? : null),
                role      = Roles.MapRole(row.TryGetValue("roleId", out var r) ? (int?)Convert.ToInt32(r) : null),
            });
        }

        /// <summary>Returns booking and payment history for a user.</summary>
        public async Task<ApiResponse> GetUserHistoryAsync(string id)
        {
            var userRow = await _db.GetUserByIdAsync(id);
            if (userRow is null) return ApiResponse.Fail("User not found");

            var numericId = Convert.ToInt32(userRow["id"]);
            var bookings  = (await _db.GetBookingsByUserIdAsync(numericId)).ToList();
            var payments  = (await _db.GetPaymentsByUserIdAsync(numericId)).ToList();

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
