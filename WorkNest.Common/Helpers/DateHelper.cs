namespace WorkNest.Common.Helpers
{
    /// <summary>
    /// Date/time serialization helper.
    /// Mirrors the Python _iso() function — returns ISO 8601 string or null.
    /// </summary>
    public static class DateHelper
    {
        /// <summary>Converts a nullable DateTime to ISO 8601 string. Returns null if value is null.</summary>
        public static string? ToIso(DateTime? value) =>
            value?.ToString("o");
    }
}
