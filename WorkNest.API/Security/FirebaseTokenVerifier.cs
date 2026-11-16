using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
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

        /// <summary>Named HttpClient (Program.cs) used to download the keys: IPv4 first, 10 s timeout.</summary>
        public const string HttpClientName = "firebase-keys";
        /// <summary>How long one download of Google's keys may take before we give up (was the 100 s default).</summary>
        public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);
        /// <summary>When Google can't be reached, the last keys we downloaded stay usable this long (Google keeps old keys valid for days).</summary>
        public static readonly TimeSpan StaleKeysMaxAge = TimeSpan.FromHours(24);
        /// <summary>After a failed download we wait this long before trying Google again (logins keep using the last good keys meanwhile).</summary>
        public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(2);

        private readonly IHttpClientFactory _http;
        private readonly IMemoryCache _cache;
        private readonly ILogger<FirebaseTokenVerifier>? _logger;
        private readonly string _projectId;
        private readonly SemaphoreSlim _fetchLock = new(1, 1);
        private IList<JsonWebKey>? _lastGoodKeys;
        private DateTimeOffset _lastGoodAt;
        private DateTimeOffset _lastFailureAt;
        private string? _lastFailure;

        public const string DefaultProjectId = "work-nest-3936a";

        public FirebaseTokenVerifier(IHttpClientFactory http, IMemoryCache cache, IConfiguration config,
            ILogger<FirebaseTokenVerifier>? logger = null)
        {
            _http = http;
            _cache = cache;
            _logger = logger;
            var configured = config["Firebase:ProjectId"];
            _projectId = !string.IsNullOrWhiteSpace(configured) ? configured.Trim() : DefaultProjectId;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_projectId);

        /// <summary>The verified identity (email lower-case, email_verified, sign_in_provider, uid) or an error message.</summary>
        public async Task<(FirebaseIdentity? Identity, string? Error)> VerifyAsync(string idToken, CancellationToken ct = default)
        {
            if (!IsConfigured) return (null, "Sign-in verification is not configured on the server (Firebase:ProjectId).");
            IList<JsonWebKey> keys;
            try { keys = await GetKeysAsync(ct); }
            catch (KeysUnavailableException ex) { return (null, ex.Message); }
            try
            {
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
            catch (Exception ex)
            {
                // The token itself was rejected (bad signature, wrong project, malformed...): say why in the log.
                return (null, $"Sign-in could not be verified ({ex.GetType().Name}: {Shorten(ex.Message)}).");
            }
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

        /// <summary>
        /// Google's signing keys. Fresh copy cached for 1 h; one download at a time, at most 10 s. If Google can't be
        /// reached, the last good keys keep working for up to 24 h (and we retry Google every 2 min), so a network blip or
        /// an API restart no longer stops every sign-in. Throws KeysUnavailableException (with the real reason) only when
        /// there are no usable keys at all.
        /// </summary>
        private async Task<IList<JsonWebKey>> GetKeysAsync(CancellationToken ct)
        {
            if (_cache.TryGetValue(KeysCacheKey, out IList<JsonWebKey>? cached) && cached != null) return cached;

            await _fetchLock.WaitAsync(ct);
            try
            {
                if (_cache.TryGetValue(KeysCacheKey, out cached) && cached != null) return cached;   // another request just fetched them

                var now = DateTimeOffset.UtcNow;
                var stale = _lastGoodKeys != null && now - _lastGoodAt < StaleKeysMaxAge ? _lastGoodKeys : null;
                if (stale != null && now - _lastFailureAt < RetryAfterFailure) return stale;   // Google failed moments ago: don't wait on it again

                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(FetchTimeout);
                    string json;
                    try { json = await _http.CreateClient(HttpClientName).GetStringAsync(KeysUrl, timeout.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new TimeoutException($"no answer from www.googleapis.com within {FetchTimeout.TotalSeconds:0} s");
                    }
                    var keys = new JsonWebKeySet(json).Keys;
                    if (keys == null || keys.Count == 0) throw new InvalidOperationException("Google returned no signing keys");

                    _cache.Set(KeysCacheKey, keys, TimeSpan.FromHours(1)); // Google rotates these keys roughly daily
                    _lastGoodKeys = keys;
                    _lastGoodAt = now;
                    if (_lastFailure != null)
                        _logger?.LogInformation("Firebase signing keys downloaded again after an earlier failure ({Failure}).", _lastFailure);
                    _lastFailure = null;
                    return keys;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _lastFailureAt = now;
                    _lastFailure = $"{ex.GetType().Name}: {Shorten(Describe(ex))}";
                    if (stale != null)
                    {
                        _logger?.LogWarning("Could not download Firebase signing keys from {Url} ({Failure}); using the keys from {At:u}.",
                            KeysUrl, _lastFailure, _lastGoodAt);
                        return stale;
                    }
                    _logger?.LogError("Could not download Firebase signing keys from {Url} ({Failure}). Every Firebase sign-in fails until " +
                        "this server can reach www.googleapis.com over HTTPS.", KeysUrl, _lastFailure);
                    throw new KeysUnavailableException(
                        $"Sign-in could not be verified: the server could not download Google's signing keys ({_lastFailure}).");
                }
            }
            finally { _fetchLock.Release(); }
        }

        /// <summary>
        /// Handler for the key download: tries the IPv4 addresses first, then IPv6, each with a short connect timeout.
        /// A server with IPv6 switched on but no working IPv6 route otherwise hangs on every call to Google.
        /// </summary>
        public static SocketsHttpHandler CreateKeysHandler() => new()
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = async (context, ct) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
                Exception? last = null;
                foreach (var address in addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        attempt.CancelAfter(TimeSpan.FromSeconds(4));
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), attempt.Token);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        socket.Dispose();
                        last = new IOException($"connect to {address} failed: {ex.Message}", ex);
                    }
                    catch { socket.Dispose(); throw; }
                }
                throw last ?? new IOException($"no addresses found for {context.DnsEndPoint.Host}");
            }
        };

        private static string Describe(Exception ex)
        {
            var parts = new List<string>();
            for (var e = ex; e != null && parts.Count < 3; e = e.InnerException) parts.Add(e.Message);
            return string.Join(" -> ", parts.Distinct());
        }

        private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "...";

        private sealed class KeysUnavailableException : Exception
        {
            public KeysUnavailableException(string message) : base(message) { }
        }
    }
}
