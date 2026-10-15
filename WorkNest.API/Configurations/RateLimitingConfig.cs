using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using WorkNest.API.Extensions;
using WorkNest.API.Security;

namespace WorkNest.API.Configurations
{
    /// <summary>
    /// Built-in ASP.NET Core rate limiting, tunable from appsettings "RateLimiting" (all values optional):
    ///   Enabled (true) · Global: AnonymousPerMinute (120 per IP), AuthenticatedPerMinute (600 per user)
    ///   Auth: PermitLimit (10), WindowSeconds (60) — per IP (login / register / sync / mobile access verify)
    ///   GoogleLogin: PerAccountPermitLimit (20), PerAccountWindowSeconds (60) — per verified Firebase account, checked in
    ///     AuthController (SignInAccountLimiter); PerIpPermitLimit (120), PerIpWindowSeconds (60) — loose per-IP cap. The app
    ///     exchanges its token on every start and hourly refresh and a coworking Wi-Fi shares one IP, so 10 per IP was
    ///     far too low. Not counted against the Global anonymous allowance of that IP.
    ///   PublicForms: PermitLimit (5), WindowSeconds (600) — per IP for anonymous callers, per user when signed in;
    ///     StaffPermitLimit (60) per signed-in staff user (walk-in inquiries added from the app / CMS)
    ///   Pdf: ConcurrentLimit (4, shared by the whole API), QueueLimit (10), PerMinute (30 per user / IP)
    /// Logged-in callers are partitioned by user id (JWT NameIdentifier, else email); anonymous callers by
    /// Connection.RemoteIpAddress, which UseForwardedHeaders has already resolved from X-Forwarded-For — only for
    /// requests from ReverseProxy:TrustedProxies (the worknestpk.com relay), see ReverseProxyConfig.
    /// Machine endpoints (PayFast notify, access-status export) carry [DisableRateLimiting].
    /// </summary>
    public static class RateLimitingConfig
    {
        public const string AuthPolicy = "auth";
        public const string GoogleLoginPolicy = "google-login";
        public const string PublicFormsPolicy = "public-forms";
        public const string PdfPolicy = "pdf";

        public static IServiceCollection AddRateLimitingConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            var section = configuration.GetSection("RateLimiting");
            int anonPerMinute = Positive(section.GetValue<int?>("Global:AnonymousPerMinute"), 120);
            int userPerMinute = Positive(section.GetValue<int?>("Global:AuthenticatedPerMinute"), 600);
            int authLimit = Positive(section.GetValue<int?>("Auth:PermitLimit"), 10);
            int authWindow = Positive(section.GetValue<int?>("Auth:WindowSeconds"), 60);
            int googleIpLimit = Positive(section.GetValue<int?>("GoogleLogin:PerIpPermitLimit"), 120);
            int googleIpWindow = Positive(section.GetValue<int?>("GoogleLogin:PerIpWindowSeconds"), 60);
            int googleAccountLimit = Positive(section.GetValue<int?>("GoogleLogin:PerAccountPermitLimit"), 20);
            int googleAccountWindow = Positive(section.GetValue<int?>("GoogleLogin:PerAccountWindowSeconds"), 60);
            int formsLimit = Positive(section.GetValue<int?>("PublicForms:PermitLimit"), 5);
            int formsWindow = Positive(section.GetValue<int?>("PublicForms:WindowSeconds"), 600);
            int formsStaffLimit = Positive(section.GetValue<int?>("PublicForms:StaffPermitLimit"), 60);
            int pdfConcurrent = Positive(section.GetValue<int?>("Pdf:ConcurrentLimit"), 4);
            int pdfQueue = Math.Max(0, section.GetValue<int?>("Pdf:QueueLimit") ?? 10);
            int pdfPerMinute = Positive(section.GetValue<int?>("Pdf:PerMinute"), 30);

            services.AddSingleton(new SignInAccountLimiter(googleAccountLimit, googleAccountWindow,
                configuration.GetValue("RateLimiting:Enabled", true)));

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                // General limiter for every endpoint, chained with the API-wide PDF concurrency gate
                // (the gate only applies to endpoints tagged with the "pdf" policy).
                var general = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                {
                    // google-login has its own per-IP cap and per-account limit; a busy shared Wi-Fi must not spend the
                    // whole IP's anonymous allowance on token refreshes.
                    if (HasPolicy(ctx, GoogleLoginPolicy)) return RateLimitPartition.GetNoLimiter("google-login");
                    var userKey = UserKey(ctx);
                    return userKey != null
                        ? RateLimitPartition.GetSlidingWindowLimiter("u:" + userKey, _ => SlidingPerMinute(userPerMinute))
                        : RateLimitPartition.GetSlidingWindowLimiter("ip:" + IpKey(ctx), _ => SlidingPerMinute(anonPerMinute));
                });
                var pdfGate = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                    IsPdfEndpoint(ctx)
                        ? RateLimitPartition.GetConcurrencyLimiter("pdf-concurrency", _ => new ConcurrencyLimiterOptions
                        {
                            PermitLimit = pdfConcurrent,
                            QueueLimit = pdfQueue,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                        })
                        : RateLimitPartition.GetNoLimiter("none"));
                options.GlobalLimiter = PartitionedRateLimiter.CreateChained(general, pdfGate);

                // Login / register / sync — per IP.
                options.AddPolicy(AuthPolicy, ctx =>
                    RateLimitPartition.GetFixedWindowLimiter("ip:" + IpKey(ctx), _ => Fixed(authLimit, authWindow)));

                // google-login — loose per-IP cap; the per-account limit runs in AuthController after the token verified.
                options.AddPolicy(GoogleLoginPolicy, ctx =>
                    RateLimitPartition.GetFixedWindowLimiter("ip:" + IpKey(ctx), _ => Fixed(googleIpLimit, googleIpWindow)));

                // Contact + book-tour forms — per IP when anonymous, per user when signed in (staff get a higher limit).
                options.AddPolicy(PublicFormsPolicy, ctx =>
                {
                    var (key, limit) = PublicFormsPartition(ctx, formsLimit, formsStaffLimit);
                    return RateLimitPartition.GetFixedWindowLimiter(key, _ => Fixed(limit, formsWindow));
                });

                // PDF / Excel generation — per user (or IP when anonymous); concurrency is gated globally above.
                options.AddPolicy(PdfPolicy, ctx =>
                {
                    var key = UserKey(ctx) is { } u ? "u:" + u : "ip:" + IpKey(ctx);
                    return RateLimitPartition.GetFixedWindowLimiter(key, _ => Fixed(pdfPerMinute, 60));
                });

                options.OnRejected = async (context, cancellationToken) =>
                {
                    var response = context.HttpContext.Response;
                    string wait = "a few seconds";
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        int seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                        response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                        wait = seconds == 1 ? "1 second" : $"{seconds} seconds";
                    }
                    response.StatusCode = StatusCodes.Status429TooManyRequests;
                    await response.WriteAsJsonAsync(new
                    {
                        isSuccessful = false,
                        message = TooManyMessage(wait)
                    }, cancellationToken);
                };
            });

            return services;
        }

        /// <summary>Must run after UseRouting + UseAuthentication (endpoint policies, user partitions) and before MapControllers.</summary>
        public static void UseRateLimitingWithConfig(this WebApplication app)
        {
            if (app.Configuration.GetValue("RateLimiting:Enabled", true))
            {
                app.UseRateLimiter();
            }
        }

        private static string? UserKey(HttpContext ctx)
        {
            if (ctx.User.Identity?.IsAuthenticated != true) return null;
            var id = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? ctx.User.FindFirst(ClaimTypes.Email)?.Value
                     ?? ctx.User.Identity?.Name;
            return string.IsNullOrWhiteSpace(id) ? null : id.Trim().ToLowerInvariant();
        }

        private static string IpKey(HttpContext ctx) =>
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        /// <summary>
        /// Partition and limit of the contact / book-tour forms: signed-in staff (walk-in inquiries) "staff:&lt;user&gt;" with
        /// the staff limit, other signed-in users "u:&lt;user&gt;" with the normal limit, anonymous callers "ip:&lt;ip&gt;".
        /// </summary>
        public static (string Key, int Limit) PublicFormsPartition(HttpContext ctx, int formsLimit, int staffLimit)
        {
            var userKey = UserKey(ctx);
            if (userKey is null) return ("ip:" + IpKey(ctx), formsLimit);
            return ctx.User.IsStaff() ? ("staff:" + userKey, staffLimit) : ("u:" + userKey, formsLimit);
        }

        private static bool HasPolicy(HttpContext ctx, string policy) =>
            string.Equals(ctx.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName, policy, StringComparison.Ordinal);

        private static bool IsPdfEndpoint(HttpContext ctx) => HasPolicy(ctx, PdfPolicy);

        /// <summary>
        /// For a 429 sent by an endpoint itself: sets Retry-After (when known) and returns the same body as the rate
        /// limiter's own refusals.
        /// </summary>
        public static object TooManyRequestsBody(HttpResponse response, TimeSpan? retryAfter)
        {
            string wait = "a few seconds";
            if (retryAfter is { } r)
            {
                int seconds = Math.Max(1, (int)Math.Ceiling(r.TotalSeconds));
                response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                wait = seconds == 1 ? "1 second" : $"{seconds} seconds";
            }
            return new { isSuccessful = false, message = TooManyMessage(wait) };
        }

        private static string TooManyMessage(string wait) => $"Too many requests — please wait {wait} and try again.";

        private static SlidingWindowRateLimiterOptions SlidingPerMinute(int permits) => new()
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0
        };

        private static FixedWindowRateLimiterOptions Fixed(int permits, int windowSeconds) => new()
        {
            PermitLimit = permits,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = 0
        };

        private static int Positive(int? value, int fallback) => value is > 0 ? value.Value : fallback;
    }
}
