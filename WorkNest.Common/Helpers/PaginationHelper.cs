namespace WorkNest.Common.Helpers
{
    /// <summary>
    /// In-memory pagination and search helper.
    /// Mirrors the Python paginate() function exactly:
    /// filters by search string across all string values, then slices by page/limit.
    /// </summary>
    public static class PaginationHelper
    {
        /// <summary>
        /// Filters a list by a search term (case-insensitive, across all string properties),
        /// then returns a page slice and the total count before slicing.
        /// </summary>
        public static (IEnumerable<T> Items, int Total) Paginate<T>(
            IEnumerable<T> source,
            int page,
            int limit,
            string search = "")
        {
            var list = source.ToList();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.ToLowerInvariant();
                list = list.Where(item =>
                {
                    if (item is IDictionary<string, object?> dict)
                        return dict.Values.Any(v =>
                            v is not null && v.ToString()!.ToLowerInvariant().Contains(s));

                    return item!.GetType()
                         .GetProperties()
                         .Any(p =>
                         {
                             var val = p.GetValue(item);
                             return val is not null && val.ToString()!.ToLowerInvariant().Contains(s);
                         });
                }).ToList();
            }

            var total = list.Count;
            var start = (page - 1) * limit;
            var items = list.Skip(start).Take(limit);

            return (items, total);
        }
    }
}
