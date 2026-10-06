using System;
using System.Security.Claims;
using WorkNest.Common.Constants;

namespace WorkNest.API.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        public static int? GetLocationId(this ClaimsPrincipal user)
        {
            if (user == null) return null;

            foreach (var claimType in new[] { "location_id", "LocationId", "locationId", ClaimTypes.GroupSid })
            {
                var val = user.FindFirst(claimType)?.Value;
                if (!string.IsNullOrWhiteSpace(val) && int.TryParse(val, out var id))
                {
                    return id;
                }
            }
            return null;
        }

        public static string GetRole(this ClaimsPrincipal user)
        {
            if (user == null) return Roles.General;

            var roleClaim = user.FindFirst(ClaimTypes.Role)?.Value
                         ?? user.FindFirst("role")?.Value;

            if (string.IsNullOrWhiteSpace(roleClaim)) return Roles.General;

            if (Roles.ReverseMap.ContainsKey(roleClaim.ToLowerInvariant()))
            {
                return roleClaim.ToLowerInvariant();
            }

            if (int.TryParse(roleClaim, out var roleId))
            {
                return Roles.MapRole(roleId);
            }

            return roleClaim;
        }

        public static bool IsSuperAdmin(this ClaimsPrincipal user)
        {
            return Roles.IsSuperAdmin(user.GetRole());
        }

        /// <summary>
        /// Admin / super admin — the only roles that may see machine-admin users on the access machines.
        /// Same role names as the [Authorize] attributes.
        /// </summary>
        public static bool IsAdminOrSuperAdmin(this ClaimsPrincipal user)
        {
            return user.IsInRole("admin") || user.IsInRole("Admin") || user.IsInRole("super_admin") || user.IsInRole("SuperAdmin")
                   || user.IsSuperAdmin() || user.GetRole() == "admin";
        }

        public static bool IsLocationBoundRole(this ClaimsPrincipal user)
        {
            return Roles.IsLocationBoundRole(user.GetRole());
        }

        /// <summary>
        /// Staff = admin, super admin, sales executive, receptionist (same role names as the [Authorize] attributes).
        /// Customers (role "general") are not staff.
        /// </summary>
        public static bool IsStaff(this ClaimsPrincipal user)
        {
            if (user == null) return false;
            return user.IsInRole("admin") || user.IsInRole("Admin") || user.IsInRole("super_admin") || user.IsInRole("SuperAdmin")
                   || user.IsInRole("sales_executive") || user.IsInRole("SalesExecutive") || user.IsInRole("receptionist") || user.IsInRole("Receptionist");
        }

        /// <summary>
        /// The signed-in user's email, taken ONLY from the JWT (never from a client header such as x-user-email).
        /// </summary>
        public static string? GetEmail(this ClaimsPrincipal user)
        {
            if (user == null || user.Identity?.IsAuthenticated != true) return null;
            var email = user.FindFirst(ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
            return string.IsNullOrWhiteSpace(email) ? null : email;
        }
    }
}
