namespace WorkNest.Application.Services
{
    /// <summary>
    /// DateTimes read from SQL come back with Kind = Unspecified. Most business columns (StartOn, EndOn, DueOn,
    /// IssuedOn, ValidityDate, BillingPeriod*, HIK machine times …) hold Pakistan wall-clock values, but the audit
    /// columns below are written with SYSUTCDATETIME() / GETUTCDATE() and hold UTC. Marking those as
    /// DateTimeKind.Utc lets the JSON writer append "Z" only to real UTC values, so the browser shows both kinds correctly.
    /// </summary>
    public static class DbDateTimeKinds
    {
        private static readonly HashSet<string> UtcColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            "CreatedOn", "UpdatedOn", "CreatedDate", "UpdatedDate", "CreatedAt", "UpdatedAt",
            "BookingDate", "PaidOn", "SentAt", "ReadAt", "UsedOn", "RevokedOn", "RevokedAt", "GrantedAt",
            "ProcessedOn", "LastAttemptAt", "NextRetryAt", "CancelledDate", "ReleasedOn", "ForfeitedOn", "UploadedOn"
        };

        public static bool IsUtcColumn(string? columnName) =>
            !string.IsNullOrEmpty(columnName) && UtcColumns.Contains(columnName);

        /// <summary>Returns the value with Kind = Utc when the column is a known UTC audit column; otherwise unchanged.</summary>
        public static object? Normalize(string? columnName, object? value) =>
            value is DateTime dt && dt.Kind == DateTimeKind.Unspecified && IsUtcColumn(columnName)
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                : value;
    }
}
