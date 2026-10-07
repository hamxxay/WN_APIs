using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WorkNest.API.Extensions
{
    /// <summary>
    /// Pages a list across several locations for staff assigned to more than one (WN_UserLocations).
    /// The list procedures filter by a single @LocationId, so each location is read and the results are
    /// merged newest first. With one location it is a plain pass-through.
    /// </summary>
    public static class MultiLocationList
    {
        private static readonly string[] DateKeys =
            { "CreatedOn", "CreatedDate", "BookedOn", "BookingDate", "IssuedOn", "QuotationDate", "Date" };

        public static async Task<(List<T> Items, int Total)> FetchAsync<T>(
            IReadOnlyList<int?> locations, int page, int limit,
            Func<int, int, int?, Task<(IEnumerable<T> Items, int Total)>> fetch)
        {
            if (page <= 0) page = 1;
            if (limit <= 0) limit = 10;
            if (locations.Count <= 1)
            {
                var single = await fetch(page, limit, locations.Count == 0 ? null : locations[0]);
                return (single.Items.ToList(), single.Total);
            }

            var all = new List<T>();
            int total = 0;
            foreach (var loc in locations)
            {
                var part = await fetch(1, page * limit, loc); // enough rows from each to fill the requested page
                all.AddRange(part.Items);
                total += part.Total;
            }
            var pageItems = all
                .OrderByDescending(x => DateOf(x) ?? DateTime.MinValue)
                .Skip((page - 1) * limit)
                .Take(limit)
                .ToList();
            return (pageItems, total);
        }

        /// <summary>First date-like field found on a row (dictionary or object), for newest-first ordering.</summary>
        public static DateTime? DateOf(object? row)
        {
            if (row is null) return null;
            foreach (var key in DateKeys)
            {
                object? v = null;
                if (row is IDictionary<string, object?> d)
                {
                    var k = d.Keys.FirstOrDefault(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
                    if (k != null) v = d[k];
                }
                else if (row is IDictionary nd && nd.Contains(key)) v = nd[key];
                else v = row.GetType().GetProperty(key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase)?.GetValue(row);

                if (v is DateTime dt) return dt;
                if (v is DateTimeOffset dto) return dto.UtcDateTime;
                if (v != null && DateTime.TryParse(v.ToString(), out var parsed)) return parsed;
            }
            return null;
        }
    }
}
