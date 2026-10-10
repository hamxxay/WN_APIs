namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Pakistani CNIC rule shared with the web and mobile apps: after removing spaces and dashes,
    /// exactly 13 digits (0-9). Anything else (letters, dots, …) is invalid. Validation only —
    /// callers keep storing/normalising the value the way they always have.
    /// </summary>
    public static class Cnic
    {
        public const string ErrorMessage = "CNIC must be 13 digits (numbers only), e.g. 35202-1234567-1.";

        /// <summary>True when <paramref name="value"/> is exactly 13 digits once spaces and dashes are removed.</summary>
        public static bool IsValid(string? value)
        {
            if (value == null) return false;
            int digits = 0;
            foreach (var c in value)
            {
                if (c == ' ' || c == '-') continue;
                if (c < '0' || c > '9') return false;
                digits++;
            }
            return digits == 13;
        }

        /// <summary>
        /// For optional fields: null/blank is allowed (returns null); otherwise returns
        /// <see cref="ErrorMessage"/> when invalid, or null when valid.
        /// </summary>
        public static string? Validate(string? value) =>
            string.IsNullOrWhiteSpace(value) || IsValid(value) ? null : ErrorMessage;

        /// <summary>The 13 digits of a valid CNIC (spaces/dashes removed).</summary>
        public static string Digits(string value) => value.Replace(" ", "").Replace("-", "");
    }
}
