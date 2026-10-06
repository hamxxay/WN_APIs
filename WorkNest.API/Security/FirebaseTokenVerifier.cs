using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace WorkNest.API.Security
{
    /// <summary>
    /// Verifies a Firebase Authentication ID token (from user.getIdToken() on the website / app):
    /// RS256 signature against Google's published keys, issuer and audience = the Firebase project,
    /// not expired. Only a verified token proves the person really signed in with Firebase.
    /// </summary>
    public class FirebaseTokenVerifier
    {
        private const string KeysUrl = "https://www.googleapis.com/service_accounts/v1/jwk/securetoken@system.gserviceaccount.com";
        private const string KeysCacheKey = "firebase-signing-keys";

        private readonly IHttpClientFactory _http;
        private readonly IMemoryCache _cache;
        private readonly string _projectId;

        public FirebaseTokenVerifier(IHttpClientFactory http, IMemoryCache cache, IConfiguration config)
        {
            _http = http;
            _cache = cache;
            _projectId = config["Firebase:ProjectId"] ?? string.Empty;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_projectId);

        /// <summary>Returns the verified email (lower-case) or an error message.</summary>
        public async Task<(string? Email, string? Error)> VerifyAsync(string idToken, CancellationToken ct = default)
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
                var email = principal.FindFirst("email")?.Value;
                if (string.IsNullOrWhiteSpace(email)) return (null, "The sign-in token has no email.");
                return (email.Trim().ToLowerInvariant(), null);
            }
            catch (SecurityTokenExpiredException) { return (null, "Your sign-in has expired. Please sign in again."); }
            catch (Exception) { return (null, "Sign-in could not be verified. Please sign in again."); }
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
