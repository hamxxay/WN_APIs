using System.Security.Cryptography;
using System.Text;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Password storage (AUTH_TEMP_01): "pbkdf2$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;" (PBKDF2, SHA-256, 100,000
    /// iterations, 16-byte random salt, 32-byte hash). Verify also accepts the old plain-text values still stored for
    /// existing staff (compared in constant time); those are not migrated.
    /// </summary>
    public static class PasswordHasher
    {
        public const string Prefix = "pbkdf2$";
        public const int Iterations = 100_000;
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        public static bool IsHashed(string? stored) =>
            !string.IsNullOrEmpty(stored) && stored.StartsWith(Prefix, StringComparison.Ordinal);

        public static string Hash(string password)
        {
            ArgumentException.ThrowIfNullOrEmpty(password);
            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
            return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        /// <summary>True when <paramref name="given"/> is the stored password (hashed or legacy plain text). Never throws.</summary>
        public static bool Verify(string? given, string? stored)
        {
            if (string.IsNullOrWhiteSpace(given) || string.IsNullOrWhiteSpace(stored)) return false;
            var givenBytes = Encoding.UTF8.GetBytes(given);

            if (!IsHashed(stored))
                return CryptographicOperations.FixedTimeEquals(givenBytes, Encoding.UTF8.GetBytes(stored));

            try
            {
                var parts = stored.Split('$');
                if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations < 1 || iterations > 10_000_000)
                    return false;
                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                if (expected.Length == 0) return false;
                var actual = Rfc2898DeriveBytes.Pbkdf2(givenBytes, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
