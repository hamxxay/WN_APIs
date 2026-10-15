using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace WorkNest.API.Security
{
    /// <summary>
    /// Who a verified Firebase ID token belongs to. EmailVerified is the token's email_verified claim: true for Google
    /// sign-ins and for email / password accounts whose owner opened Firebase's verification link; false for an email /
    /// password account anyone could have created for that address (see AuthService.CheckFirebaseSignInAsync).
    /// </summary>
    public sealed record FirebaseIdentity(string Email, bool EmailVerified, string? SignInProvider, string? Uid);

    /// <summary>
    /// Verifies a Firebase Authentication ID token (from user.getIdToken() on the website / app):
    /// RS256 signature against Google's published keys, issuer and audience = the Firebase project,
    /// not expired. Only a verified token proves the person really signed in with Firebase; it does NOT prove they
    /// own the email unless EmailVerified is true.
    /// </summary>
    public class FirebaseTokenVerifier
    {
        private const string KeysUrl = "https://www.googleapis.com/service_accounts/v1/jwk/securetoken@system.gserviceaccount.com";
        /// <summary>IMemoryCache entry holding Google's signing keys (IList&lt;JsonWebKey&gt;), refreshed hourly.</summary>
        public const string KeysCacheKey = "firebase-signing-keys";

        private readonly IHttpClientFactory _http;
        private readonly IMemoryCache _cache;
        private readonly string _projectId;

        public const string DefaultProjectId = "work-nest-3936a";

        public FirebaseTokenVerifier(IHttpClientFactory http, IMemoryCache cache, IConfiguration config)
        {
            _http = http;
            _cache = cache;
            var configured = config["Firebase:ProjectId"];
            _projectId = !string.IsNullOrWhiteSpace(configured) ? configured.Trim() : DefaultProjectId;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_projectId);

        /// <summary>The verified identity (email lower-case, email_verified, sign_in_provider, uid) or an error message.</summary>
        public async Task<(FirebaseIdentity? Identity, string? Error)> VerifyAsync(string idToken, CancellationToken ct = default)
        {
            if (!IsConfigured) return (null, "Sign-in verification is not configured on the server (Firebase:ProjectId).");
            try
            {
                var keys = await GetKeysAsync(ct);
                var parameters = new TokenValidationParameters
                {
                    ValidIssuer = $"https://securetoken.google.com/{_projectId}",
                    ValidAudience = _projectId,
                    IssuerSigningKeys = keys,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                    ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
                };
                var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(idToken, parameters, out _);
                var identity = ReadIdentity(principal);
                return identity is null ? (null, "The sign-in token has no email.") : (identity, null);
            }
            catch (SecurityTokenExpiredException) { return (null, "Your sign-in has expired. Please sign in again."); }
            catch (Exception) { return (null, "Sign-in could not be verified. Please sign in again."); }
        }

        /// <summary>
        /// Reads the identity from an already validated Firebase token: email (lower-case), email_verified (missing or
        /// unreadable = false), firebase.sign_in_provider ("password", "google.com", ...) and the uid (user_id / sub).
        /// Null when the token has no email.
        /// </summary>
        public static FirebaseIdentity? ReadIdentity(ClaimsPrincipal principal)
        {
            var email = principal.FindFirst("email")?.Value;
            if (string.IsNullOrWhiteSpace(email)) return null;

            var verifiedClaim = principal.FindFirst("email_verified")?.Value;
            var emailVerified = bool.TryParse(verifiedClaim, out var v) && v;

            string? provider = null;
            var firebaseClaim = principal.FindFirst("firebase")?.Value;
            if (!string.IsNullOrWhiteSpace(firebaseClaim))
            {
                try
                {
                    using var doc = JsonDocument.Parse(firebaseClaim);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("sign_in_provider", out var p) && p.ValueKind == JsonValueKind.String)
                        provider = p.GetString();
                }
                catch (JsonException) { /* provider is for logs only */ }
            }

            var uid = principal.FindFirst("user_id")?.Value ?? principal.FindFirst("sub")?.Value;
            return new FirebaseIdentity(email.Trim().ToLowerInvariant(), emailVerified,
                string.IsNullOrWhiteSpace(provider) ? null : provider, string.IsNullOrWhiteSpace(uid) ? null : uid);
        }

        private async Task<IList<JsonWebKey>> GetKeysAsync(CancellationToken ct)
        {
            if (_cache.TryGetValue(KeysCacheKey, out IList<JsonWebKey>? cached) && cached != null) return cached;
            var json = await _http.CreateClient().GetStringAsync(KeysUrl, ct);
            var keys = new JsonWebKeySet(json).Keys;
            _cache.Set(KeysCacheKey, keys, TimeSpan.FromHours(1)); // Google rotates these keys roughly daily
            return keys;
        }
    }
}
