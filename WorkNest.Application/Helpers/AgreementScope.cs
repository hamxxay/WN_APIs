using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Agreements list for location-bound staff (admins / sales executives): only the agreements of their locations,
    /// plus any agreement without a location (same rule as opening one agreement, RecordScope). An agreement's
    /// location is the booked space's, else the quoted space's.
    /// </summary>
    public static class AgreementScope
    {
        /// <summary>@LocationIds for WN_Agreements_GetList: "1,3"; "0" when the user has no location (matches none).</summary>
        public static string LocationCsv(IEnumerable<int> locationIds)
        {
            var ids = locationIds.Where(i => i > 0).Distinct().OrderBy(i => i).ToList();
            return ids.Count == 0 ? "0" : string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>The row's agreement id ("Id" column), or null.</summary>
        public static int? RowId(IDictionary<string, object?> row)
        {
            var key = row.Keys.FirstOrDefault(k => string.Equals(k, "Id", StringComparison.OrdinalIgnoreCase));
            if (key == null || row[key] is null) return null;
            try { return Convert.ToInt32(row[key], CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        /// <summary>
        /// Keeps the rows the user may see and returns one page of them (newest first, as read) with the new total.
        /// Used before QA_06 is run, when the procedure can't filter by location yet. <paramref name="locationById"/>:
        /// each agreement's location (missing / null = no location: kept).
        /// </summary>
        public static (List<IDictionary<string, object?>> Rows, int Total) FilterAndPage(
            IEnumerable<IDictionary<string, object?>> rows,
            IReadOnlyDictionary<int, int?> locationById,
            IReadOnlyCollection<int> allowedLocations,
            int page, int limit)
        {
            if (page < 1) page = 1;
            if (limit < 1) limit = 10;
            var visible = rows.Where(r =>
            {
                var id = RowId(r);
                if (id == null || !locationById.TryGetValue(id.Value, out var loc) || loc == null) return true;
                return allowedLocations.Contains(loc.Value);
            }).ToList();
            var pageRows = visible.Skip((page - 1) * limit).Take(limit).ToList();
            return (pageRows, visible.Count);
        }
    }
}
