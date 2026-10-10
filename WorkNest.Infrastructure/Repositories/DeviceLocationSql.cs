using Microsoft.Data.SqlClient;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// WN_HIK_Devices.LocationId (added by WN_HIK_Devices_LocationId.txt) decides which location a machine belongs to.
    /// Databases where that script hasn't run yet have no column, so the check runs once and every machine query
    /// leaves the column out until it exists (machines then stay visible to everyone, as before).
    /// </summary>
    internal static class DeviceLocationSql
    {
        private static bool? _hasColumn;

        public static async Task<bool> HasColumnAsync(SqlConnection open)
        {
            if (_hasColumn == true) return true;  // once present it never goes away; a missing column is re-checked
            await using var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.WN_HIK_Devices', 'LocationId') IS NULL THEN 0 ELSE 1 END;", open);
            _hasColumn = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
            return _hasColumn.Value;
        }

        /// <summary>
        /// " AND (alias.LocationId IS NULL OR alias.LocationId IN (...))" for location-bound callers; "" for everyone else
        /// or before the column exists. Machines without a location stay visible. Ids are ints from claims, never text.
        /// </summary>
        public static string Filter(bool hasColumn, string alias, IReadOnlyCollection<int>? locationIds)
        {
            if (!hasColumn || locationIds is null) return "";
            var ids = locationIds.Distinct().ToList();
            var list = ids.Count == 0 ? "-1" : string.Join(",", ids);
            return $" AND ({alias}.LocationId IS NULL OR {alias}.LocationId IN ({list}))";
        }

        /// <summary>
        /// " AND column IN (machines of those locations + unassigned machines)" for a device-id column (scans, grants);
        /// "" for everyone else or before the column exists.
        /// </summary>
        public static string DeviceIdIn(bool hasColumn, string column, IReadOnlyCollection<int>? locationIds)
        {
            if (!hasColumn || locationIds is null) return "";
            return $" AND {column} IN (SELECT hdx.Id FROM dbo.WN_HIK_Devices hdx WITH (NOLOCK) WHERE 1 = 1{Filter(true, "hdx", locationIds)})";
        }

        /// <summary>
        /// " AND EXISTS (access grant on one of those machines)" for a member row (WN_HIK_Employees alias); "" otherwise.
        /// A member belongs to a location when they have access on one of its machines.
        /// </summary>
        public static string MemberIn(bool hasColumn, string employeeIdColumn, IReadOnlyCollection<int>? locationIds)
        {
            if (!hasColumn || locationIds is null) return "";
            return $" AND EXISTS (SELECT 1 FROM dbo.WN_HIK_AccessGrants gx WITH (NOLOCK) WHERE gx.employee_id = {employeeIdColumn}{DeviceIdIn(true, "gx.device_id", locationIds)})";
        }
    }
}
