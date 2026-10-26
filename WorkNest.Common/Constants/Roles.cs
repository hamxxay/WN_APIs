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
        /// <summary>Read-only, all locations: sees only the CEO dashboard (api/ceo/*). Only a Super Admin gives this role.</summary>
        public const string Ceo            = "ceo";
        /// <summary>
        /// Read-only, tied to the locations a Super Admin assigns (like an admin): sees only the Sales Manager dashboard
        /// (api/sales-manager/*). Only a Super Admin gives this role.
        /// </summary>
        public const string SalesManager   = "sales_manager";
        /// <summary>
        /// Door access only, tied to the locations a Super Admin assigns (like a Sales Manager): finds a person by CNIC on
        /// a confirmed booking of those locations, adds them to the room, enrols face / fingerprint, removes or restores
        /// their door access (api/access-officer/*). Only a Super Admin gives this role.
        /// </summary>
        public const string AccessOfficer  = "access_officer";
        /// <summary>
        /// Full access to network configuration and monitoring (UniFi and Grandstream GWN), tied to the locations
        /// a Super Admin assigns. Only a Super Admin gives this role.
        /// </summary>
        public const string NetworkEngineer = "network_engineer";

        public const int SuperAdminId     = 1;
        public const int AdminId          = 2;
        public const int GeneralId        = 14;
        public const int SalesExecutiveId = 16;
        public const int CeoId            = 17;
        public const int SalesManagerId   = 18;
        public const int AccessOfficerId  = 19;
        public const int NetworkEngineerId = 20;

        public static readonly Dictionary<int, string> RoleMap = new()
        {
            { SuperAdminId,     SuperAdmin     },
            { AdminId,          Admin          },
            { GeneralId,        General        },
            { SalesExecutiveId, SalesExecutive },
            { CeoId,            Ceo            },
            { SalesManagerId,   SalesManager   },
            { AccessOfficerId,  AccessOfficer  },
            { NetworkEngineerId, NetworkEngineer },
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
            { Ceo,                      CeoId            },
            { SalesManager,             SalesManagerId   },
            { "salesmanager",           SalesManagerId   },
            { "sales manager",          SalesManagerId   },
            { AccessOfficer,            AccessOfficerId  },
            { "accessofficer",          AccessOfficerId  },
            { "access officer",         AccessOfficerId  },
            { NetworkEngineer,          NetworkEngineerId },
            { "networkengineer",        NetworkEngineerId },
            { "network engineer",       NetworkEngineerId },
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

        /// <summary>
        /// Admins, Sales Executives, Sales Managers, Access Officers and Network Engineers are limited to their assigned location(s)
        /// (WN_Users.LocationId plus WN_UserLocations); Super Admins and the CEO cover every location.
        /// </summary>
        public static bool IsLocationBoundRole(int? roleId) =>
            roleId == SalesExecutiveId || roleId == AdminId || roleId == SalesManagerId || roleId == AccessOfficerId || roleId == NetworkEngineerId;

        /// <summary>
        /// admin, sales_executive, sales_manager, access_officer and network_engineer are limited to their assigned location(s); super_admin
        /// and ceo are not.
        /// </summary>
        public static bool IsLocationBoundRole(string? role) =>
            string.Equals(role, SalesExecutive, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, Admin, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, SalesManager, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, AccessOfficer, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, NetworkEngineer, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Returns true if the role is SuperAdmin.</summary>
        public static bool IsSuperAdmin(int? roleId) =>
            roleId == SuperAdminId;

        /// <summary>Returns true if the role string is super_admin.</summary>
        public static bool IsSuperAdmin(string? role) =>
            string.Equals(role, SuperAdmin, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Returns true if the role is CEO (read-only CEO dashboard, all locations).</summary>
        public static bool IsCeo(int? roleId) =>
            roleId == CeoId;

        /// <summary>Returns true if the role string is ceo.</summary>
        public static bool IsCeo(string? role) =>
            string.Equals(role, Ceo, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Returns true if the role is Sales Manager (read-only Sales Manager dashboard, own locations).</summary>
        public static bool IsSalesManager(int? roleId) =>
            roleId == SalesManagerId;

        /// <summary>Returns true if the role string is sales_manager.</summary>
        public static bool IsSalesManager(string? role) =>
            string.Equals(role, SalesManager, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Returns true if the role is Access Officer (door access by CNIC only, own locations).</summary>
        public static bool IsAccessOfficer(int? roleId) =>
            roleId == AccessOfficerId;

        /// <summary>Returns true if the role string is access_officer.</summary>
        public static bool IsAccessOfficer(string? role) =>
            string.Equals(role, AccessOfficer, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Returns true if the role is Network Engineer (full access to network pages, own locations).</summary>
        public static bool IsNetworkEngineer(int? roleId) =>
            roleId == NetworkEngineerId;

        /// <summary>Returns true if the role string is network_engineer.</summary>
        public static bool IsNetworkEngineer(string? role) =>
            string.Equals(role, NetworkEngineer, System.StringComparison.OrdinalIgnoreCase);
    }
}
