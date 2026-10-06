using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.ExternalServices.Unifi
{
    /// <summary>
    /// HTTP client for the UniFi Site Manager cloud API (api.ui.com) and the console Network API
    /// reached through the Site Manager connector. Port of apiGet / apiGetAll / net in "Unifi UI" server.js.
    /// The Network API requires an API key from the account that owns the console.
    /// </summary>
    public class UnifiClient : IUnifiClient
    {
        private static readonly TimeSpan CloudTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan ConsoleTimeout = TimeSpan.FromSeconds(30);

        private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
        private readonly string _apiKey;
        private readonly Uri _apiBase;

        public UnifiClient(IConfiguration configuration)
        {
            _apiKey = configuration["Unifi:ApiKey"]?.Trim() ?? string.Empty;
            var apiBase = configuration["Unifi:ApiBase"];
            _apiBase = new Uri(string.IsNullOrWhiteSpace(apiBase) ? "https://api.ui.com" : apiBase.Trim());
        }

        public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

        public async Task<JsonElement> GetAsync(string path, IDictionary<string, string>? query = null, CancellationToken ct = default)
        {
            EnsureConfigured();
            var url = path;
            if (query != null && query.Count > 0)
                url += "?" + string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

            using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(_apiBase, url));
            using var resp = await SendAsync(req, CloudTimeout, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                var hint = resp.StatusCode == HttpStatusCode.Unauthorized ? " (check Unifi:ApiKey)"
                    : resp.StatusCode == HttpStatusCode.TooManyRequests ? " (rate limited)" : "";
                throw new InvalidOperationException($"{path} → HTTP {(int)resp.StatusCode}{hint} {(text.Length > 200 ? text[..200] : text)}");
            }
            return JsonSerializer.Deserialize<JsonElement>(text);
        }

        public async Task<List<JsonElement>> GetAllAsync(string path, IDictionary<string, string>? query = null, CancellationToken ct = default)
        {
            var output = new List<JsonElement>();
            string? nextToken = null;
            for (var page = 0; page < 50; page++)
            {
                var q = new Dictionary<string, string> { ["pageSize"] = "500" };
                if (query != null) foreach (var kv in query) q[kv.Key] = kv.Value;
                if (!string.IsNullOrEmpty(nextToken)) q["nextToken"] = nextToken;

                var json = await GetAsync(path, q, ct);
                if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    output.AddRange(data.EnumerateArray());
                nextToken = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("nextToken", out var nt) && nt.ValueKind == JsonValueKind.String
                    ? nt.GetString() : null;
                if (string.IsNullOrEmpty(nextToken)) break;
            }
            return output;
        }

        public async Task<JsonElement> NetworkAsync(string consoleId, string path, object? body = null, HttpMethod? method = null, CancellationToken ct = default)
        {
            EnsureConfigured();
            method ??= body != null ? HttpMethod.Post : HttpMethod.Get;
            var url = new Uri(_apiBase, $"/v1/connector/consoles/{Uri.EscapeDataString(consoleId)}/proxy/network{path}");

            using var req = new HttpRequestMessage(method, url);
            if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await SendAsync(req, ConsoleTimeout, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var hint = resp.StatusCode == HttpStatusCode.Forbidden ? " (the API key must belong to the console owner)" : "";
                throw new InvalidOperationException($"Console API {path} → HTTP {(int)resp.StatusCode}{hint}");
            }
            return JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync(ct));
        }

        // ---- Transport ------------------------------------------------------------

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, TimeSpan timeout, CancellationToken ct)
        {
            req.Headers.TryAddWithoutValidation("X-API-KEY", _apiKey);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                return await _http.SendAsync(req, cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"{req.RequestUri?.AbsolutePath} → timed out after {timeout.TotalSeconds:0}s");
            }
        }

        private void EnsureConfigured()
        {
            if (!IsConfigured) throw new InvalidOperationException("Unifi:ApiKey is not set in appsettings");
        }
    }
}
