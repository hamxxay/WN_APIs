using System.Text.RegularExpressions;

namespace WorkNest.Common.Helpers
{
    /// <summary>
    /// GUID and identifier resolution helpers used across controllers and repositories.
    /// </summary>
    public static class GuidHelper
    {
        private static readonly Regex GuidPattern =
            new(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
                RegexOptions.Compiled);

        /// <summary>Returns true if the string is a valid GUID format.</summary>
        public static bool IsGuid(string? value) =>
            !string.IsNullOrWhiteSpace(value) && GuidPattern.IsMatch(value);

        /// <summary>Returns true if the string looks like an email address.</summary>
        public static bool IsEmail(string? value) =>
            !string.IsNullOrWhiteSpace(value) && value.Contains('@');

        /// <summary>Generates a random reference number with a given prefix.</summary>
        public static string GenerateRef(string prefix) =>
            $"{prefix}-{Random.Shared.Next(100000, 999999)}";
    }
}
