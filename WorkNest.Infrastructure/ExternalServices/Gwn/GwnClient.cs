using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.ExternalServices.Gwn
{
    /// <summary>
    /// GWN Cloud Northbound API client — port of tokenRequest / getToken / gwn() in WN_Grandstream/server.js.
    /// Token: POST https://{Domain}/oauth/token (form, password grant with sha256hex(md5hex(password))), renewed 2 minutes
    /// before it expires (refresh_token grant, else a new login); concurrent callers share one renewal. Calls:
    /// https://{Domain}/oapi/{path}?access_token&amp;appID&amp;timestamp&amp;signature with a JSON body; HTTP 401 or retCode
    /// 50003 = token problem: a new token and one retry. Errors never contain the URL (it carries the token).
    /// </summary>
    public sealed class GwnClient : IGwnClient
    {
        private static readonly TimeSpan TokenTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(25);
        private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(2);

        private readonly GwnSettings _settings;
        private readonly HttpClient _http;
        private readonly ILogger<GwnClient> _logger;
        private readonly Func<DateTime> _now;
        private readonly SemaphoreSlim _tokenLock = new(1, 1);

        private string _access = "";
        private string _refresh = "";
        private DateTime _expiresAt = DateTime.MinValue;

        public GwnClient(IConfiguration configuration, ILogger<GwnClient> logger)
            : this(GwnSettings.From(configuration), logger, null, null) { }

        /// <summary>For tests: a fake HTTP handler and clock.</summary>
        public GwnClient(GwnSettings settings, ILogger<GwnClient> logger, HttpMessageHandler? handler, Func<DateTime>? now = null)
        {
            _settings = settings;
            _logger = logger;
            _http = handler == null ? new HttpClient() : new HttpClient(handler);
            _http.Timeout = Timeout.InfiniteTimeSpan;
            _now = now ?? (() => DateTime.UtcNow);
        }

        public bool IsConfigured => _settings.HasCredentials;

        private string Base => $"https://{_settings.Domain}";

        // ---- token ----------------------------------------------------------------

        private async Task TokenRequestAsync(Dictionary<string, string> form, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TokenTimeout);
            HttpResponseMessage resp;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/oauth/token") { Content = new FormUrlEncodedContent(form) };
                req.Headers.Accept.ParseAdd("application/json");
                resp = await _http.SendAsync(req, cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new GwnApiException($"Can't reach {_settings.Domain} (timed out)");
            }
            catch (HttpRequestException ex)
            {
                throw new GwnApiException($"Can't reach {_settings.Domain} ({ex.HttpRequestError})");
            }
            using var _ = resp;
            var text = await resp.Content.ReadAsStringAsync(ct);
            JsonElement body = default;
            try { body = JsonSerializer.Deserialize<JsonElement>(text); } catch (JsonException) { }
            string Field(string name) => body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var access = Field("access_token");
            if (!resp.IsSuccessStatusCode || access.Length == 0)
            {
                var why = new[] { Field("error_description"), Field("error"), Field("msg") }.FirstOrDefault(x => x.Length > 0) ?? $"HTTP {(int)resp.StatusCode}";
                var hint = resp.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized ? " (check Gwn:Username / Password / AppId / SecretKey)" : "";
                throw new GwnApiException($"GWN login failed: {why}{hint}");
            }
            _access = access;
            var refresh = Field("refresh_token");
            if (refresh.Length > 0) _refresh = refresh;
            var expires = body.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var sec) && sec > 0 ? sec : 3599;
            _expiresAt = _now().AddSeconds(expires);
        }

        private Task PasswordLoginAsync(CancellationToken ct) => TokenRequestAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = _settings.Username,
            ["password"] = GwnSigning.PasswordHash(_settings.Password),
            ["client_id"] = _settings.AppId,
            ["client_secret"] = _settings.SecretKey
        }, ct);

        private async Task RenewAsync(CancellationToken ct)
        {
            if (_refresh.Length > 0)
            {
                try
                {
                    await TokenRequestAsync(new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = _refresh,
                        ["client_id"] = _settings.AppId,
                        ["client_secret"] = _settings.SecretKey
                    }, ct);
                    return;
                }
                catch (GwnApiException ex)
                {
                    _logger.LogInformation("[gwn] token refresh failed, logging in again: {Error}", ex.Message);
                    _refresh = "";
                }
            }
            await PasswordLoginAsync(ct);
        }

        /// <summary>A valid access token. <paramref name="rejected"/> = the token GWN just refused (renewed unless already replaced).</summary>
        private async Task<string> GetTokenAsync(string? rejected, CancellationToken ct)
        {
            if (!IsConfigured) throw new GwnApiException("Grandstream is not set up on this server.");
            if (rejected == null && _access.Length > 0 && _now() < _expiresAt - RenewBefore) return _access;
            await _tokenLock.WaitAsync(ct);
            try
            {
                var stillValid = _access.Length > 0 && _now() < _expiresAt - RenewBefore;
                if (stillValid && (rejected == null || _access != rejected)) return _access;
                if (rejected != null && _access == rejected) _access = "";
                await RenewAsync(ct);
                return _access;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        // ---- signed call ----------------------------------------------------------

        public Task<JsonElement> CallAsync(string path, object? body = null, CancellationToken ct = default) => CallAsync(path, body, false, ct);

        private static bool IsAction(string apiPath) =>
            apiPath.Contains("block", StringComparison.OrdinalIgnoreCase) || apiPath.Contains("reboot", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// GWN sometimes sends a second JSON object after the answer (seen on statistics/ap/client with type 2): the first
        /// value is the answer. Null when even that cannot be read.
        /// </summary>
        internal JsonElement? FirstJsonValue(string text, string apiPath)
        {
            try
            {
                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), new JsonReaderOptions { AllowMultipleValues = true });
                var first = JsonElement.ParseValue(ref reader);
                var rest = text.Length > reader.BytesConsumed ? Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text), (int)reader.BytesConsumed, (int)Math.Min(200, Encoding.UTF8.GetByteCount(text) - reader.BytesConsumed)) : "";
                _logger.LogDebug("[gwn] {Path}: extra data after the answer ignored: {Rest}", apiPath, rest);
                return first;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private async Task<JsonElement> CallAsync(string path, object? body, bool retried, CancellationToken ct, string? rejected = null)
        {
            var apiPath = path.TrimStart('/');
            var access = await GetTokenAsync(rejected, ct);
            var json = body switch { null => "", string s => s, _ => JsonSerializer.Serialize(body) };
            var ts = new DateTimeOffset(_now()).ToUnixTimeMilliseconds();
            var signature = GwnSigning.Signature(access, _settings.AppId, _settings.SecretKey, ts, json);
            var url = $"{Base}/oapi/{apiPath}?access_token={Uri.EscapeDataString(access)}&appID={Uri.EscapeDataString(_settings.AppId)}&timestamp={ts}&signature={signature}";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(CallTimeout);
            HttpResponseMessage resp;
            try
            {
                using var req = new HttpRequestMessage(json.Length > 0 ? HttpMethod.Post : HttpMethod.Get, url);
                if (json.Length > 0) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                req.Headers.Accept.ParseAdd("application/json");
                resp = await _http.SendAsync(req, cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new GwnApiException($"Can't reach {_settings.Domain} (timed out)");
            }
            catch (HttpRequestException ex)
            {
                throw new GwnApiException($"Can't reach {_settings.Domain} ({ex.HttpRequestError})");
            }

            using var _ = resp;
            var text = await resp.Content.ReadAsStringAsync(ct);
            JsonElement? answer = null;
            string? parseError = null;
            try { answer = JsonSerializer.Deserialize<JsonElement>(text); } catch (JsonException ex) { parseError = ex.Message; answer = FirstJsonValue(text, apiPath); }
            int? retCode = answer is { ValueKind: JsonValueKind.Object } a && a.TryGetProperty("retCode", out var rc) && rc.ValueKind == JsonValueKind.Number && rc.TryGetInt32(out var code) ? code : null;

            var tokenProblem = resp.StatusCode == HttpStatusCode.Unauthorized || retCode == 50003;
            if (tokenProblem && !retried) return await CallAsync(path, body, true, ct, access);

            if (!resp.IsSuccessStatusCode) throw new GwnApiException($"{apiPath} → HTTP {(int)resp.StatusCode}", retCode);
            // A cut-off answer (seen now and then on the weekly statistics): reads are asked once more; actions never.
            if (answer is not { ValueKind: JsonValueKind.Object } && !retried && resp.IsSuccessStatusCode && !IsAction(apiPath))
                return await CallAsync(path, body, true, ct);
            if (answer is not { ValueKind: JsonValueKind.Object } ok)
            {
                _logger.LogWarning("[gwn] {Path} answered HTTP {Status} v{Version} len={ContentLength} chunked={Chunked} ({Type}, {Length} chars, {ParseError}): {Start}", apiPath, (int)resp.StatusCode, resp.Version, resp.Content.Headers.ContentLength, resp.Headers.TransferEncodingChunked,
                    resp.Content.Headers.ContentType?.MediaType, text.Length, parseError, text.Length > 200 ? text[..200] : text);
                throw new GwnApiException($"{apiPath} → unreadable answer");
            }
            if (retCode != 0)
            {
                var msg = ok.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(m.GetString()) ? m.GetString() : "error";
                throw new GwnApiException($"{apiPath} → {msg} (code {retCode?.ToString() ?? "?"})", retCode);
            }
            return ok.TryGetProperty("data", out var data) ? data.Clone() : default;
        }
    }
}
