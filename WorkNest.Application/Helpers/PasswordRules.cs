using System.Security.Cryptography;

namespace WorkNest.Application.Helpers
{
    /// <summary>Rules for the password a customer chooses, and the temporary password staff hand out (AUTH_TEMP_01).</summary>
    public static class PasswordRules
    {
        public const int MinLength = 8;
        public const int MaxLength = 128;

        /// <summary>Upper-case letters and digits without the look-alikes 0 O 1 I L.</summary>
        public const string TempAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        /// <summary>Null when <paramref name="newPassword"/> is acceptable, else the message to show.</summary>
        public static string? ValidateNew(string? currentPassword, string? newPassword)
        {
            if (string.IsNullOrEmpty(newPassword)) return "Enter a new password.";
            if (newPassword.Length < MinLength) return $"The new password must be at least {MinLength} characters.";
            if (newPassword.Length > MaxLength) return $"The new password must be {MaxLength} characters or fewer.";
            if (!newPassword.Any(char.IsLetter) || !newPassword.Any(char.IsDigit))
                return "The new password must contain at least one letter and one digit.";
            if (string.Equals(newPassword, currentPassword, StringComparison.Ordinal))
                return "The new password must be different from the current password.";
            return null;
        }

        /// <summary>A random temporary password like "Wn-7KQ4-PX92" (two groups of four from <see cref="TempAlphabet"/>).</summary>
        public static string GenerateTemp()
        {
            static string Group()
            {
                var chars = new char[4];
                for (var i = 0; i < chars.Length; i++) chars[i] = TempAlphabet[RandomNumberGenerator.GetInt32(TempAlphabet.Length)];
                return new string(chars);
            }
            return $"Wn-{Group()}-{Group()}";
        }

        /// <summary>True for gmail.com / googlemail.com addresses (they can also use Continue with Google).</summary>
        public static bool CanUseGoogle(string? email)
        {
            var at = email?.LastIndexOf('@') ?? -1;
            if (at < 0 || email is null) return false;
            var domain = email[(at + 1)..].Trim();
            return domain.Equals("gmail.com", StringComparison.OrdinalIgnoreCase)
                || domain.Equals("googlemail.com", StringComparison.OrdinalIgnoreCase);
        }
    }
}
