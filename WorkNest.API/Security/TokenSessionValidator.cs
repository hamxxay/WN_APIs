using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WorkNest.Application.Interfaces;
using WorkNest.Infrastructure.Security.JWT;

namespace WorkNest.API.Security
{
    /// <summary>
    /// Second check of every bearer token, after its signature and expiry (JwtBearer OnTokenValidated):
    /// - a token signed out through POST api/auth/logout is refused until it expires;
    /// - the account must still exist and be active, with the role (and, for Admins / Sales Executives / Sales
    ///   Managers, the location) the token was issued with (TokenSessionRules) — a token used to keep its role for 24 hours.
    /// A refused token is a 401, which the app answers by exchanging its Firebase sign-in at api/auth/google-login
    /// (anonymous, so not affected) and the website by sending the user to sign in.
    /// The WN_Users lookup is cached 60 seconds per email. When it fails (database unreachable) the token is
    /// allowed and a warning logged: the signature and expiry were already checked, and an outage must not sign
    /// every user out. Signed-out tokens are kept in this process's memory only (lost when the app pool recycles).
    /// </summary>
    public class TokenSessionValidator
    {
        private static readonly TimeSpan AccountCacheTime = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan LookupWarningInterval = TimeSpan.FromMinutes(1);
        private const string SessionItemKey = "WorkNest.TokenSession";

        private readonly IMemoryCache _cache;
        private readonly ILogger<TokenSessionValidator> _logger;

        public TokenSessionValidator(IMemoryCache cache, ILogger<TokenSessionValidator> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        private sealed record TokenSession(string Key, DateTimeOffset ExpiresUtc);

        public async Task ValidateAsync(TokenValidatedContext context)
        {
            var principal = context.Principal;
            if (principal is null)
            {
                context.Fail("No identity in the token.");
                return;
            }

            var session = SessionOf(context.SecurityToken);
            if (session != null && _cache.TryGetValue(RevokedCacheKey(session.Key), out _))
            {
                context.Fail("This token was signed out.");
                return;
            }

            var email = principal.FindFirst(ClaimTypes.Email)?.Value
                        ?? principal.FindFirst("email")?.Value;
            if (string.IsNullOrWhiteSpace(email))
            {
                context.Fail("No email in the token.");
                return;
            }

            AccountState account;
            try
            {
                account = await GetAccountAsync(email, context.HttpContext.RequestServices);
            }
            catch (Exception ex) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                // Fail open: the database is unreachable, so most requests fail anyway; don't sign everyone out.
                if (!_cache.TryGetValue("auth-session-lookup-warning", out _))
                {
                    _cache.Set("auth-session-lookup-warning", true, LookupWarningInterval);
                    _logger.LogWarning(ex, "Could not check the account behind a sign-in token (WN_Users lookup failed); tokens are accepted unchecked until the database answers again (logged once a minute).");
                }
                Remember(context.HttpContext, session);
                return;
            }

            var tokenRole = principal.FindFirst(ClaimTypes.Role)?.Value ?? principal.FindFirst("role")?.Value;
            var tokenLocation = principal.FindFirst("location_id")?.Value ?? principal.FindFirst("LocationId")?.Value;
            var reason = TokenSessionRules.RejectReason(account, tokenRole, tokenLocation);
            if (reason != null)
            {
                context.Fail(reason);
                return;
            }

            Remember(context.HttpContext, session);
        }

        /// <summary>
        /// Signs out the token of this request (if it was valid): it is refused from now until it expires.
        /// Returns false when the request carried no valid token.
        /// </summary>
        public bool RevokeCurrent(HttpContext httpContext)
        {
            if (httpContext.Items[SessionItemKey] is not TokenSession session) return false;
            if (session.ExpiresUtc <= DateTimeOffset.UtcNow) return false;
            _cache.Set(RevokedCacheKey(session.Key), true, session.ExpiresUtc);
            return true;
        }

        private async Task<AccountState> GetAccountAsync(string email, IServiceProvider services)
        {
            var cacheKey = "auth-account:" + email.Trim().ToLowerInvariant();
            if (_cache.TryGetValue(cacheKey, out AccountState? cached) && cached != null) return cached;

            var db = services.GetRequiredService<IDbRepository>();
            var account = AccountState.FromUserRow(await db.GetUserByEmailAsync(email.Trim()));
            _cache.Set(cacheKey, account, AccountCacheTime);
            return account;
        }

        private static TokenSession? SessionOf(SecurityToken? token)
        {
            var (jti, encoded) = token switch
            {
                JsonWebToken jwt => (jwt.Id, jwt.EncodedToken),
                JwtSecurityToken legacy => (legacy.Id, legacy.RawData),
                _ => (token?.Id, null)
            };
            var key = TokenSessionRules.SessionKey(jti, encoded);
            if (key is null || token is null || token.ValidTo == DateTime.MinValue) return null;
            return new TokenSession(key, new DateTimeOffset(DateTime.SpecifyKind(token.ValidTo, DateTimeKind.Utc)));
        }

        private static void Remember(HttpContext httpContext, TokenSession? session)
        {
            if (session != null) httpContext.Items[SessionItemKey] = session;
        }

        private static string RevokedCacheKey(string sessionKey) => "auth-revoked:" + sessionKey;
    }
}
