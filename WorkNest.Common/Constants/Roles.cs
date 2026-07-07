namespace WorkNest.Common.Constants
{
    /// <summary>
    /// Centralized role constants matching the Python ROLE_MAP exactly.
    /// RoleId integers stored in WN_Users.RoleId column.
    /// </summary>
    public static class Roles
    {
        public const string SuperAdmin = "super_admin";
        public const string Admin      = "admin";
        public const string General    = "general";

        public const int SuperAdminId = 1;
        public const int AdminId      = 2;
        public const int GeneralId    = 14;

        public static readonly Dictionary<int, string> RoleMap = new()
        {
            { SuperAdminId, SuperAdmin },
            { AdminId,      Admin      },
            { GeneralId,    General    },
        };

        public static readonly Dictionary<string, int> ReverseMap = new()
        {
            { SuperAdmin, SuperAdminId },
            { Admin,      AdminId      },
            { General,    GeneralId    },
        };

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
    }
}
