namespace WorkNest.Common.Responses
{
    /// <summary>
    /// Mirrors the Python paginate() helper output: { data: [], total: N }.
    /// Returned by all paginated list endpoints.
    /// </summary>
    public class PaginatedResponse<T>
    {
        /// <summary>The page slice of items.</summary>
        public IEnumerable<T> Data { get; set; } = [];

        /// <summary>Total count before pagination (used by frontend for page count).</summary>
        public int Total { get; set; }
    }
}
