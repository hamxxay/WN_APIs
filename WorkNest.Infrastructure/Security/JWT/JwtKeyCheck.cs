using System.Text.Json;

namespace WorkNest.Infrastructure.Security.JWT
{
    /// <summary>
    /// Start-up check of JwtSettings:SecretKey. Everyone who knows (or guesses) the key can sign a token for any
    /// user and role, super admin included, so on the live server it must be long and random.
    /// </summary>
    public static class JwtKeyCheck
    {
        public const int MinimumLength = 32;

        /// <summary>What is wrong with the key (empty when it looks random enough). Never includes the key itself.</summary>
        public static List<string> Weaknesses(string? key, string? developmentKey)
        {
            var problems = new List<string>();
            if (string.IsNullOrEmpty(key))
            {
                problems.Add("it is empty");
                return problems;
            }
            if (key.Length < MinimumLength)
                problems.Add($"it is shorter than {MinimumLength} characters");
            if (!string.IsNullOrEmpty(developmentKey) && string.Equals(key, developmentKey, StringComparison.Ordinal))
                problems.Add("it is the same key as appsettings.Development.json");
            if (LooksLikePhrase(key))
                problems.Add("it looks like readable text rather than random bytes");
            return problems;
        }

        /// <summary>Words people put in hand-made keys ("WorkNestSuperSecretKey2024!"); 5+ letters, so random keys don't hit them.</summary>
        private static readonly string[] CommonWords = { "secret", "password", "worknest", "token", "signing", "changeme", "example" };

        /// <summary>
        /// Simple heuristic: nothing but letters, spaces and punctuation (a random base64 key of 32+ characters
        /// almost always contains a digit), a common word, or very few distinct characters for its length
        /// (under 3 bits per character; random base64 has about 5, random hex about 4).
        /// </summary>
        public static bool LooksLikePhrase(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (key.All(ch => char.IsLetter(ch) || char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))) return true;
            if (CommonWords.Any(w => key.Contains(w, StringComparison.OrdinalIgnoreCase))) return true;
            return ShannonBitsPerChar(key) < 3.0;
        }

        /// <summary>Shannon entropy of the characters of <paramref name="s"/>, in bits per character.</summary>
        public static double ShannonBitsPerChar(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            double bits = 0;
            foreach (var group in s.GroupBy(ch => ch))
            {
                double p = (double)group.Count() / s.Length;
                bits -= p * Math.Log2(p);
            }
            return bits;
        }

        /// <summary>JwtSettings:SecretKey of an appsettings.Development.json file, or null when it is absent or unreadable.</summary>
        public static string? DevelopmentKeyFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(path),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                return doc.RootElement.TryGetProperty("JwtSettings", out var jwt)
                       && jwt.ValueKind == JsonValueKind.Object
                       && jwt.TryGetProperty("SecretKey", out var key)
                       && key.ValueKind == JsonValueKind.String
                    ? key.GetString()
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
