using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Cross-site sign-in (api/auth/handoff): a user signed in on one site (worknestpk.com or the CMS) is sent to the
    /// other already signed in. The signed-in site asks for a one-time code, the other site redeems it for a new session,
    /// the same as a normal sign-in returns. See <see cref="Helpers.AuthHandoffRules"/> for the rules.
    /// </summary>
    public interface IAuthHandoffService
    {
        /// <summary>
        /// A new code for the signed-in user (by the JWT's email) to open <paramref name="target"/> ("cms" | "web"):
        /// data { code, expiresInSeconds, target }. Fails (400) for an unknown target, (403) "cms" for a customer.
        /// Throws <see cref="AuthHandoffMissingException"/> before AUTH_HANDOFF_01 has run.
        /// </summary>
        Task<AuthHandoffResult> CreateAsync(string email, string? target, CancellationToken ct = default);

        /// <summary>
        /// Spends the code (whatever the outcome: a code is presented once) and, when it is valid for
        /// <paramref name="target"/>, signs its user in exactly like api/auth/login (same response body).
        /// Throws <see cref="AuthHandoffMissingException"/> before AUTH_HANDOFF_01 has run.
        /// </summary>
        Task<AuthHandoffResult> RedeemAsync(string? code, string? target, CancellationToken ct = default);
    }

    /// <summary>The answer and the HTTP status AuthHandoffController sends it with.</summary>
    public sealed record AuthHandoffResult(int StatusCode, ApiResponse Response);

    /// <summary>
    /// dbo.WN_AuthHandoff_Create / dbo.WN_AuthHandoff_Redeem (script Database/Auth/AUTH_HANDOFF_01_login_handoff). Only
    /// the SHA-256 of a code is stored. SQL errors 208 / 2812 (table / procedure missing) throw
    /// <see cref="AuthHandoffMissingException"/>.
    /// </summary>
    public interface IAuthHandoffRepository
    {
        /// <summary>Stores a new code for the user, valid for <paramref name="ttlSeconds"/> (database clock).</summary>
        Task CreateAsync(int userId, byte[] codeHash, string target, int ttlSeconds, CancellationToken ct = default);

        /// <summary>
        /// Marks the code used in one atomic UPDATE (only while unused) and returns it with its user; null when there is
        /// no unused code with this hash (unknown, or already used).
        /// </summary>
        Task<AuthHandoffRedemption?> RedeemAsync(byte[] codeHash, CancellationToken ct = default);
    }

    /// <summary>
    /// A code just marked used: its target, expiry and the moment it was used (both from the database clock) and its
    /// user (Email / IsActive from WN_Users; Email null when the user row is gone).
    /// </summary>
    public sealed record AuthHandoffRedemption(int UserId, string Target, DateTime ExpiresAtUtc, DateTime UsedAtUtc,
        string? Email, bool IsActive);

    /// <summary>The database update AUTH_HANDOFF_01_login_handoff has not run: no table / procedures.</summary>
    public sealed class AuthHandoffMissingException : Exception
    {
        /// <summary>What the API answers (503). The sites then send the user to the other site's sign-in page.</summary>
        public const string UserMessage =
            "Signing in on the other WorkNest site is not available yet (database update AUTH_HANDOFF_01 needed). Please sign in there.";

        public AuthHandoffMissingException(Exception inner) : base(UserMessage, inner) { }
    }
}
