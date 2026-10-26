namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// SEC_LOC_02 (Database/Security/SEC_LOC_02_lists): the location-scoped list procedures (challan search, recent
    /// bookings, customer search, memberships, security deposit report). A procedure takes @LocationIds as a CSV:
    /// null = every location (super admin, CEO, receptionist: the same rows as the old procedure).
    /// </summary>
    public static class LocationScopeLists
    {
        public const string Script = "SEC_LOC_02";

        /// <summary>"1,2" for @LocationIds; null = every location; "-1" (matches nothing) for no locations.</summary>
        public static string? Csv(IReadOnlyCollection<int>? locationIds) =>
            locationIds == null ? null : locationIds.Count == 0 ? "-1" : string.Join(",", locationIds.Distinct());
    }

    /// <summary>
    /// A location-scoped list was asked for (a location-bound caller) but the SEC_LOC_02 procedure is not on the database
    /// (SQL error 2812). Answered 503 instead of the unscoped list; callers without a scope use the old procedure.
    /// </summary>
    public sealed class LocationScopeUpdateMissingException : Exception
    {
        // No ".sql" in the text: the CMS and the app hide any message with the word "sql" in it.
        public const string UserMessage = "Run " + LocationScopeLists.Script + " on the database.";

        public LocationScopeUpdateMissingException(Exception? inner = null) : base(UserMessage, inner) { }
    }
}
