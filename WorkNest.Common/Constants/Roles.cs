namespace WorkNest.Common.Constants
{
    /// <summary>
    /// Centralized role constants matching the Python ROLE_MAP exactly.
    /// RoleId integers stored in WN_Users.RoleId column.
    /// </summary>
    public static class Roles
    {
        public const string SuperAdmin     = "super_admin";
        public const string Admin          = "admin";
        public const string General        = "general";
        public const string SalesExecutive = "sales_executive";

        public const int SuperAdminId     = 1;
        public const int AdminId          = 2;
        public const int GeneralId        = 14;
        public const int SalesExecutiveId = 16;

        public static readonly Dictionary<int, string> RoleMap = new()
        {
            { SuperAdminId,     SuperAdmin     },
            { AdminId,          Admin          },
            { GeneralId,        General        },
            { SalesExecutiveId, SalesExecutive },
        };

        public static readonly Dictionary<string, int> ReverseMap = new(System.StringComparer.OrdinalIgnoreCase)
        {
            { SuperAdmin,               SuperAdminId     },
            { "superadmin",             SuperAdminId     },
            { "super admin",            SuperAdminId     },
            { Admin,                    AdminId          },
            { "administrator",          AdminId          },
            { General,                  GeneralId        },
            { "general_user",           GeneralId        },
            { "general user",           GeneralId        },
            { SalesExecutive,           SalesExecutiveId },
            { "salesexecutive",         SalesExecutiveId },
            { "sales executive",        SalesExecutiveId },
        };

        /// <summary>Maps a role string (or integer string) to RoleId integer.</summary>
        public static int ParseRoleId(string? role, int defaultRoleId = GeneralId)
        {
            if (string.IsNullOrWhiteSpace(role)) return defaultRoleId;
            var trimmed = role.Trim();
            if (int.TryParse(trimmed, out var num))
            {
                if (RoleMap.ContainsKey(num)) return num;
                return num;
            }
            var key = trimmed.ToLower().Replace("-", "_");
            return ReverseMap.TryGetValue(key, out var r) ? r : defaultRoleId;
        }

        /// <summary>Maps a nullable integer RoleId to its string name. Defaults to "general".</summary>
        public static string MapRole(int? roleId)
        {
            if (roleId is null) return General;
            return RoleMap.TryGetValue(roleId.Value, out var name) ? name : General;
        }

        /// <summary>
        /// Extracts role from a DB row, trying multiple possible column names
        /// since different SPs alias RoleId differently.
        /// </summary>
        public static string FromRow(IDictionary<string, object?> row)
        {
            foreach (var key in new[] { "RoleId", "Roles_Int", "Role", "UserRoleId" })
            {
                if (row.TryGetValue(key, out var v) && v is not null)
                {
                    if (int.TryParse(v.ToString(), out var id))
                        return MapRole(id);
                    var s = v.ToString()!;
                    if (ReverseMap.ContainsKey(s)) return s;
                }
            }
            return General;
        }

        /// <summary>Returns true if the role string is admin-level.</summary>
        public static bool IsAdminRole(string role) =>
            role == Admin || role == SuperAdmin;

        /// <summary>Returns true if the role requires single-location binding (Admin, SalesExecutive).</summary>
        public static bool IsLocationBoundRole(int? roleId) =>
            roleId == AdminId || roleId == SalesExecutiveId;

        /// <summary>Returns true if the role string requires single-location binding (admin, sales_executive).</summary>
        public static bool IsLocationBoundRole(string? role) =>
            string.Equals(role, Admin, System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, SalesExecutive, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Returns true if the role is SuperAdmin.</summary>
        public static bool IsSuperAdmin(int? roleId) =>
            roleId == SuperAdminId;

        /// <summary>Returns true if the role string is super_admin.</summary>
        public static bool IsSuperAdmin(string? role) =>
            string.Equals(role, SuperAdmin, System.StringComparison.OrdinalIgnoreCase);
    }
}
