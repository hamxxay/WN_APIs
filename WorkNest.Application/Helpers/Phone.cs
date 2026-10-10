namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Phone number rule shared with the web and mobile apps: only digits, spaces, dashes,
    /// parentheses and a single leading '+' are allowed, and the number must contain 11–15 digits.
    /// Anything else (letters, dots, …) is invalid. Validation only — callers keep storing/normalising
    /// the value the way they always have.
    /// </summary>
    public static class Phone
    {
        public const string ErrorMessage = "Phone number must be at least 11 digits (numbers only), e.g. 03001234567.";

        public const int MinDigits = 11;
        public const int MaxDigits = 15;

        /// <summary>True when <paramref name="value"/> uses only allowed characters and has 11–15 digits.</summary>
        public static bool IsValid(string? value)
        {
            if (value == null) return false;
            var s = value.Trim();
            int digits = 0;
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c >= '0' && c <= '9') { digits++; continue; }
                if (c == ' ' || c == '-' || c == '(' || c == ')') continue;
                if (c == '+' && i == 0) continue;
                return false;
            }
            return digits >= MinDigits && digits <= MaxDigits;
        }

        /// <summary>
        /// For optional fields: null/blank is allowed (returns null); otherwise returns
        /// <see cref="ErrorMessage"/> when invalid, or null when valid.
        /// </summary>
        public static string? Validate(string? value) =>
            string.IsNullOrWhiteSpace(value) || IsValid(value) ? null : ErrorMessage;
    }
}
