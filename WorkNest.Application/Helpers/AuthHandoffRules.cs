using System.Security.Cryptography;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Cross-site sign-in codes (api/auth/handoff): 32 random bytes, base64url (43 characters), valid
    /// <see cref="TtlSeconds"/> seconds for one target site and usable once. Only their SHA-256 is stored, so the table
    /// alone cannot sign anyone in. The redeem marks the code used before these rules run, so a code shown to the wrong
    /// site, or too late, is spent as well.
    /// </summary>
    public static class AuthHandoffRules
    {
        /// <summary>The staff portal (cms.worknestpk.com): staff only.</summary>
        public const string TargetCms = "cms";
        /// <summary>The public website (worknestpk.com): everyone.</summary>
        public const string TargetWeb = "web";
        public const int TtlSeconds = 60;
        private const int CodeBytes = 32;

        /// <summary>"cms" / "web" (any case, trimmed); null for anything else.</summary>
        public static string? NormalizeTarget(string? target)
        {
            var t = target?.Trim().ToLowerInvariant();
            return t is TargetCms or TargetWeb ? t : null;
        }

        /// <summary>A new code: 32 random bytes, base64url without padding.</summary>
        public static string NewCode() => Base64Url(RandomNumberGenerator.GetBytes(CodeBytes));

        /// <summary>SHA-256 of the code text as sent (trimmed); null for a blank or implausibly long code.</summary>
        public static byte[]? HashCode(string? code)
        {
            var c = code?.Trim();
            if (string.IsNullOrEmpty(c) || c.Length > 128) return null;
            return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(c));
        }

        /// <summary>Only staff (any role other than general) may open the CMS; anyone may open the website.</summary>
        public static bool MayOpen(string role, string target) =>
            target == TargetWeb || (target == TargetCms && !string.Equals(role, Roles.General, StringComparison.OrdinalIgnoreCase));

        /// <summary>Null when the just-spent code signs its user in on <paramref name="target"/>; else why not (for the log).</summary>
        public static string? RejectReason(AuthHandoffRedemption? redemption, string target)
        {
            if (redemption is null) return "Unknown or already used code.";
            if (redemption.UsedAtUtc >= redemption.ExpiresAtUtc) return "The code has expired.";
            if (!string.Equals(redemption.Target, target, StringComparison.OrdinalIgnoreCase)) return "The code is for the other site.";
            if (string.IsNullOrWhiteSpace(redemption.Email)) return "The user no longer exists.";
            if (!redemption.IsActive) return "The user is deactivated.";
            return null;
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
