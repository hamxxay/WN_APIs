namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// App door access is tied to the signed-in account through the email the company registered for the access user:
    /// only that account may verify and unlock. Without an email on record there is nothing to tie it to (anyone who
    /// knows the person's name and CNIC could open their door), so the app is refused until the front desk adds one.
    /// </summary>
    public static class MobileDoorAccount
    {
        public const string NoEmailOnRecord =
            "Your company has not added an email for you yet. Ask the front desk to add your email, then sign in with it to use door access.";

        public const string WrongAccount = "Sign in with the email your company registered for you to unlock doors.";

        /// <summary>Why this account may not use the access user's door access; null when it may.</summary>
        public static string? Refusal(string? personEmail, string? accountEmail)
        {
            if (string.IsNullOrWhiteSpace(personEmail)) return NoEmailOnRecord;
            return string.Equals(personEmail.Trim(), accountEmail?.Trim(), StringComparison.OrdinalIgnoreCase) ? null : WrongAccount;
        }
    }
}
