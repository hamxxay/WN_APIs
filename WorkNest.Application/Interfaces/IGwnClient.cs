using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Talks to the Grandstream GWN Cloud Northbound API (GDMS Networking): OAuth token (password grant, renewed before it
    /// expires), signed calls to https://{Domain}/oapi/{path}, a new token and one retry on HTTP 401 / retCode 50003.
    /// Port of gwn() in WN_Grandstream/server.js. Never puts the App ID's secret key or the password in an error message.
    /// </summary>
    public interface IGwnClient
    {
        /// <summary>True when Gwn:AppId, SecretKey, Username and Password are all set.</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Calls one GWN path (e.g. "v1.0.0/ap/list"). A non-null body is sent as JSON with POST (a string is sent as is),
        /// else GET. Returns the answer's <c>data</c> (JsonValueKind.Undefined when it has none). Throws
        /// <see cref="GwnApiException"/> when GWN answers an error.
        /// </summary>
        Task<JsonElement> CallAsync(string path, object? body = null, CancellationToken ct = default);
    }

    /// <summary>GWN could not be reached or answered an error (retCode != 0). The message is safe to show.</summary>
    public class GwnApiException : InvalidOperationException
    {
        public GwnApiException(string message, int? retCode = null, Exception? inner = null) : base(message, inner) => RetCode = retCode;

        /// <summary>GWN's retCode (null for HTTP / network failures).</summary>
        public int? RetCode { get; }
    }

    /// <summary>The Gwn section of appsettings (never logged or returned).</summary>
    public sealed class GwnSettings
    {
        public bool Enabled { get; init; } = true;
        public string Domain { get; init; } = "www.gwn.cloud";
        public string AppId { get; init; } = "";
        public string SecretKey { get; init; } = "";
        public string Username { get; init; } = "";
        public string Password { get; init; } = "";
        public int RefreshSeconds { get; init; } = 60;
        /// <summary>
        /// Network → locations used while dbo.WN_GWN_NetworkLocations cannot be read (GWN_01 not run):
        /// Gwn:DefaultNetworkLocations "347087:2" (pairs separated by commas); default 347087 → 2 (F-7).
        /// </summary>
        public IReadOnlyList<(long NetworkId, int LocationId)> DefaultNetworkLocations { get; init; } = new[] { (347087L, 2) };

        public bool HasCredentials =>
            AppId.Length > 0 && SecretKey.Length > 0 && Username.Length > 0 && Password.Length > 0;

        private static IReadOnlyList<(long, int)> ParsePairs(string text) =>
            text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => p.Split(':', StringSplitOptions.TrimEntries))
                .Where(p => p.Length == 2 && long.TryParse(p[0], out var n) && n > 0 && int.TryParse(p[1], out var l) && l > 0)
                .Select(p => (long.Parse(p[0]), int.Parse(p[1]))).ToList();

        public static GwnSettings From(IConfiguration configuration)
        {
            var s = configuration.GetSection("Gwn");
            var domain = (s["Domain"] ?? "").Trim();
            if (domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) domain = domain[8..];
            else if (domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) domain = domain[7..];
            domain = domain.TrimEnd('/');
            return new GwnSettings
            {
                Enabled = !string.Equals(s["Enabled"], "false", StringComparison.OrdinalIgnoreCase),
                Domain = domain.Length == 0 ? "www.gwn.cloud" : domain,
                AppId = (s["AppId"] ?? "").Trim(),
                SecretKey = (s["SecretKey"] ?? "").Trim(),
                Username = (s["Username"] ?? "").Trim(),
                Password = s["Password"] ?? "",
                RefreshSeconds = Math.Max(30, int.TryParse(s["RefreshSeconds"], out var rs) ? rs : 60),
                DefaultNetworkLocations = s["DefaultNetworkLocations"] is { } pairs ? ParsePairs(pairs) : new[] { (347087L, 2) }
            };
        }
    }

    /// <summary>GWN's hashing rules (API reference "Signature Computation" and "Get token").</summary>
    public static class GwnSigning
    {
        public static string Sha256Hex(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

        public static string Md5Hex(string text) =>
            Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

        /// <summary>The password sent with the password grant: sha256hex(md5hex(password)).</summary>
        public static string PasswordHash(string password) => Sha256Hex(Md5Hex(password ?? ""));

        /// <summary>
        /// signature = sha256hex("&amp;access_token=T&amp;appID=A&amp;secretKey=S&amp;timestamp=TS&amp;" + (body ? sha256hex(body) + "&amp;" : "")).
        /// </summary>
        public static string Signature(string accessToken, string appId, string secretKey, long timestamp, string? body)
        {
            var text = $"&access_token={accessToken}&appID={appId}&secretKey={secretKey}&timestamp={timestamp}&"
                       + (string.IsNullOrEmpty(body) ? "" : Sha256Hex(body) + "&");
            return Sha256Hex(text);
        }
    }
}
