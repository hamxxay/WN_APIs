using Microsoft.Data.SqlClient;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Branch routing (BR_01_branch_routing) for the inline queries that count tour inquiries and complaints (sidebar
    /// badges, role dashboard). Tour inquiries: dbo.WN_Contacts.LocationId, added by BR_01; until it exists the check
    /// leaves the filter out (everyone counts every inquiry, as before). Complaints: dbo.WN_Complaints.Branch is a
    /// location name (case and spaces ignored); a complaint without a branch, or with a branch that is no location,
    /// belongs to everyone. Location ids are ints from claims, never user text.
    /// </summary>
    internal static class BranchSql
    {
        private static bool? _contactsHaveLocation;

        public static async Task<bool> ContactsHaveLocationAsync(SqlConnection open)
        {
            if (_contactsHaveLocation == true) return true;  // once present it never goes away; a missing column is re-checked
            await using var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.WN_Contacts', 'LocationId') IS NULL THEN 0 ELSE 1 END;", open);
            _contactsHaveLocation = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
            return _contactsHaveLocation.Value;
        }

        private static string IdList(IReadOnlyCollection<int> ids)
        {
            var l = ids.Distinct().ToList();
            return l.Count == 0 ? "-1" : string.Join(",", l);
        }

        /// <summary>
        /// " AND (alias.LocationId IS NULL OR alias.LocationId IN (...))" for location-bound staff; "" for everyone else
        /// or before BR_01. Inquiries without a branch stay visible.
        /// </summary>
        public static string Contacts(bool hasColumn, string alias, IReadOnlyCollection<int>? locationIds) =>
            !hasColumn || locationIds is null ? "" : $" AND ({alias}.LocationId IS NULL OR {alias}.LocationId IN ({IdList(locationIds)}))";

        /// <summary>The location key of a branch / location name: trimmed, lower case.</summary>
        private static string Key(string expression) => $"LOWER(LTRIM(RTRIM(CAST({expression} AS NVARCHAR(100)))))";

        /// <summary>
        /// " AND (no location has this Branch name OR one of these locations has it)" for location-bound staff; "" for
        /// everyone else.
        /// </summary>
        public static string Complaints(string branchColumn, IReadOnlyCollection<int>? locationIds) =>
            locationIds is null ? "" : $@" AND (NOT EXISTS (SELECT 1 FROM dbo.WN_Locations lbx WITH (NOLOCK) WHERE {Key("lbx.Name")} = {Key($"ISNULL({branchColumn}, N'')")})
                    OR EXISTS (SELECT 1 FROM dbo.WN_Locations lbx WITH (NOLOCK) WHERE lbx.Id IN ({IdList(locationIds)}) AND {Key("lbx.Name")} = {Key($"ISNULL({branchColumn}, N'')")}))";
    }
}
