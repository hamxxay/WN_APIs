using System.Threading.RateLimiting;

namespace WorkNest.API.Security
{
    /// <summary>
    /// Per-account limit for POST api/auth/google-login, checked after the Firebase token verified, so a made-up token
    /// can't use up someone else's allowance. The app exchanges its Firebase sign-in on every start and every hourly
    /// token refresh, and a whole coworking Wi-Fi shares one IP, so the per-IP "google-login" policy
    /// (RateLimitingConfig) is only a loose cap against abuse and this is the real limit.
    /// RateLimiting:GoogleLogin:PerAccountPermitLimit (20) per PerAccountWindowSeconds (60); always allows when
    /// RateLimiting:Enabled is false.
    /// </summary>
    public sealed class SignInAccountLimiter : IDisposable
    {
        private readonly PartitionedRateLimiter<string>? _limiter;

        public int PermitLimit { get; }
        public TimeSpan Window { get; }

        public SignInAccountLimiter(int permitLimit, int windowSeconds, bool enabled = true)
        {
            PermitLimit = Math.Max(1, permitLimit);
            Window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds));
            if (!enabled) return;
            _limiter = PartitionedRateLimiter.Create<string, string>(key =>
                RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = PermitLimit,
                    Window = Window,
                    QueueLimit = 0
                }));
        }

        /// <summary>
        /// Takes one sign-in from this account's allowance (key: the Firebase uid, else the verified email). False when
        /// it is used up; retryAfter then says how long until the window resets (when known).
        /// </summary>
        public bool TryAcquire(string accountKey, out TimeSpan? retryAfter)
        {
            retryAfter = null;
            if (_limiter is null || string.IsNullOrWhiteSpace(accountKey)) return true;
            using var lease = _limiter.AttemptAcquire(accountKey.Trim().ToLowerInvariant());
            if (lease.IsAcquired) return true;
            if (lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)) retryAfter = wait;
            return false;
        }

        public void Dispose() => _limiter?.Dispose();
    }
}
