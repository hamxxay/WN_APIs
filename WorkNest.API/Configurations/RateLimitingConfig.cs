using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace WorkNest.API.Configurations
{
    /// <summary>
    /// Built-in ASP.NET Core rate limiting, tunable from appsettings "RateLimiting" (all values optional):
    ///   Enabled (true) · Global: AnonymousPerMinute (120 per IP), AuthenticatedPerMinute (600 per user)
    ///   Auth: PermitLimit (10), WindowSeconds (60) — per IP
    ///   PublicForms: PermitLimit (5), WindowSeconds (600) — per IP
    ///   Pdf: ConcurrentLimit (4, shared by the whole API), QueueLimit (10), PerMinute (30 per user / IP)
    /// Logged-in callers are partitioned by user id (JWT NameIdentifier, else email); anonymous callers by
    /// Connection.RemoteIpAddress, which UseForwardedHeaders has already resolved from X-Forwarded-For.
    /// Machine endpoints (PayFast notify, access-status export) carry [DisableRateLimiting].
    /// </summary>
    public static class RateLimitingConfig
    {
        public const string AuthPolicy = "auth";
        public const string PublicFormsPolicy = "public-forms";
        public const string PdfPolicy = "pdf";

        public static IServiceCollection AddRateLimitingConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            var section = configuration.GetSection("RateLimiting");
            int anonPerMinute = Positive(section.GetValue<int?>("Global:AnonymousPerMinute"), 120);
            int userPerMinute = Positive(section.GetValue<int?>("Global:AuthenticatedPerMinute"), 600);
            int authLimit = Positive(section.GetValue<int?>("Auth:PermitLimit"), 10);
            int authWindow = Positive(section.GetValue<int?>("Auth:WindowSeconds"), 60);
            int formsLimit = Positive(section.GetValue<int?>("PublicForms:PermitLimit"), 5);
            int formsWindow = Positive(section.GetValue<int?>("PublicForms:WindowSeconds"), 600);
            int pdfConcurrent = Positive(section.GetValue<int?>("Pdf:ConcurrentLimit"), 4);
            int pdfQueue = Math.Max(0, section.GetValue<int?>("Pdf:QueueLimit") ?? 10);
            int pdfPerMinute = Positive(section.GetValue<int?>("Pdf:PerMinute"), 30);

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                // General limiter for every endpoint, chained with the API-wide PDF concurrency gate
                // (the gate only applies to endpoints tagged with the "pdf" policy).
                var general = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                {
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

                // Login / register / google-login / sync — per IP.
                options.AddPolicy(AuthPolicy, ctx =>
                    RateLimitPartition.GetFixedWindowLimiter("ip:" + IpKey(ctx), _ => Fixed(authLimit, authWindow)));

                // Anonymous contact + book-tour forms — per IP.
                options.AddPolicy(PublicFormsPolicy, ctx =>
                    RateLimitPartition.GetFixedWindowLimiter("ip:" + IpKey(ctx), _ => Fixed(formsLimit, formsWindow)));

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
                        message = $"Too many requests — please wait {wait} and try again."
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

        private static bool IsPdfEndpoint(HttpContext ctx) =>
            string.Equals(ctx.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName, PdfPolicy, StringComparison.Ordinal);

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
