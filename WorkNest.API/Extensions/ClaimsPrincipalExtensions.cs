using System;
using System.Linq;
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

        /// <summary>All locations the user is assigned to (location_ids claim), falling back to the single location_id.</summary>
        public static System.Collections.Generic.List<int> GetLocationIds(this ClaimsPrincipal user)
        {
            var ids = new System.Collections.Generic.List<int>();
            var csv = user?.FindFirst("location_ids")?.Value;
            if (!string.IsNullOrWhiteSpace(csv))
                foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (int.TryParse(part, out var id) && id > 0 && !ids.Contains(id)) ids.Add(id);
            var primary = user?.GetLocationId();
            if (primary is > 0 && !ids.Contains(primary.Value)) ids.Insert(0, primary.Value);
            return ids;
        }

        /// <summary>
        /// Locations a list should cover. Location-bound users: the requested one if it is theirs, otherwise all
        /// of theirs. Everyone else: the requested one (null = all locations).
        /// </summary>
        public static System.Collections.Generic.List<int?> ScopedLocations(this ClaimsPrincipal user, int? requested)
        {
            if (user?.Identity?.IsAuthenticated == true && user.IsLocationBoundRole())
            {
                var allowed = user.GetLocationIds();
                if (allowed.Count == 0) return new System.Collections.Generic.List<int?> { -1 }; // matches nothing
                if (requested is > 0 && allowed.Contains(requested.Value)) return new System.Collections.Generic.List<int?> { requested };
                return allowed.Select(x => (int?)x).ToList();
            }
            return new System.Collections.Generic.List<int?> { requested };
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
