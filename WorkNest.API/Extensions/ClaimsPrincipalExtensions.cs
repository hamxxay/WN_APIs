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

        public static bool IsLocationBoundRole(this ClaimsPrincipal user)
        {
            return Roles.IsLocationBoundRole(user.GetRole());
        }
    }
}
