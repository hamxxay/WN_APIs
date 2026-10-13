using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace WorkNest.API.Configurations
{
    /// <summary>
    /// X-Forwarded-For / X-Forwarded-Proto, believed only from the reverse proxies in "ReverseProxy:TrustedProxies"
    /// (IP addresses: a JSON array, or one comma-separated string). Default: the worknestpk.com cPanel relay
    /// (public/api/index.php), 37.27.136.233, which overwrites X-Forwarded-For with the browser's address.
    /// Without this, every website visitor shared one rate-limit bucket (the relay's address) and the e-signature
    /// record showed the relay instead of the signer. The header is never believed from anyone else, so a caller
    /// can't pick its own rate-limit bucket. A request with X-Forwarded-For from an address that is NOT trusted is
    /// logged (once per address per hour) so the relay's real outbound address can be found if it differs.
    /// </summary>
    public static class ReverseProxyConfig
    {
        public const string DefaultTrustedProxy = "37.27.136.233";
        private static readonly TimeSpan UntrustedWarningInterval = TimeSpan.FromHours(1);

        public static IServiceCollection AddReverseProxyConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            var trusted = ParseTrustedProxies(ConfiguredEntries(configuration), out _);

            services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.ForwardLimit = 1; // the relay sends exactly one address: the browser's
                // Only the listed proxies (the framework default is loopback). Never left empty: an empty list
                // would make the middleware believe X-Forwarded-For from every sender.
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
                foreach (var ip in trusted) o.KnownProxies.Add(ip);
            });
            return services;
        }

        /// <summary>Runs first in the pipeline, so rate limits, logs and the e-signature record see the real client address.</summary>
        public static void UseForwardedHeadersWithConfig(this WebApplication app)
        {
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WorkNest.API.ReverseProxy");
            ParseTrustedProxies(ConfiguredEntries(app.Configuration), out var invalid);
            if (invalid.Count > 0)
                logger.LogWarning("ReverseProxy:TrustedProxies has entries that are not IP addresses and were ignored: {Invalid}", string.Join(", ", invalid));

            var trusted = app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value.KnownProxies.ToList();
            logger.LogInformation("X-Forwarded-For is trusted only from {TrustedProxies}", string.Join(", ", trusted));

            var cache = app.Services.GetRequiredService<IMemoryCache>();
            app.Use(async (context, next) =>
            {
                var remote = context.Connection.RemoteIpAddress;
                if (remote != null && context.Request.Headers.ContainsKey("X-Forwarded-For") && !IsTrusted(remote, trusted))
                {
                    var cacheKey = "xff-untrusted:" + remote;
                    if (!cache.TryGetValue(cacheKey, out _))
                    {
                        cache.Set(cacheKey, true, UntrustedWarningInterval);
                        logger.LogWarning(
                            "Ignored X-Forwarded-For from {RemoteIp}, which is not in ReverseProxy:TrustedProxies. " +
                            "If this is the worknestpk.com relay, add this address to ReverseProxy:TrustedProxies (logged once per address per hour).",
                            remote.ToString());
                    }
                }
                await next();
            });

            app.UseForwardedHeaders();
        }

        /// <summary>"ReverseProxy:TrustedProxies" as a JSON array (or ..__0, ..__1 environment variables), or one comma-separated value.</summary>
        private static IEnumerable<string?>? ConfiguredEntries(IConfiguration configuration)
        {
            var section = configuration.GetSection("ReverseProxy:TrustedProxies");
            return section.GetChildren().Any()
                ? section.GetChildren().Select(c => c.Value)
                : section.Value?.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// The trusted proxy addresses from configuration; entries that are not IP addresses go to <paramref name="invalid"/>.
        /// No valid entry (setting absent or empty) means the default relay address.
        /// </summary>
        public static List<IPAddress> ParseTrustedProxies(IEnumerable<string?>? entries, out List<string> invalid)
        {
            invalid = new List<string>();
            var trusted = new List<IPAddress>();
            foreach (var raw in entries ?? Enumerable.Empty<string?>())
            {
                var entry = raw?.Trim();
                if (string.IsNullOrEmpty(entry)) continue;
                if (!IPAddress.TryParse(entry, out var ip))
                {
                    invalid.Add(entry);
                    continue;
                }
                if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
                if (!trusted.Contains(ip)) trusted.Add(ip);
            }
            if (trusted.Count == 0) trusted.Add(IPAddress.Parse(DefaultTrustedProxy));
            return trusted;
        }

        /// <summary>Same match as the forwarded-headers middleware: exact address, IPv4-mapped IPv6 counted as IPv4.</summary>
        public static bool IsTrusted(IPAddress remote, IReadOnlyCollection<IPAddress> trusted)
        {
            if (trusted.Contains(remote)) return true;
            return remote.IsIPv4MappedToIPv6 && trusted.Contains(remote.MapToIPv4());
        }
    }
}
