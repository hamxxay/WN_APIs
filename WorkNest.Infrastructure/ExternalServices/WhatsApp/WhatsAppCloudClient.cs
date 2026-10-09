using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.WhatsApp;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.ExternalServices.WhatsApp
{
    /// <summary>
    /// WhatsApp Cloud API (graph.facebook.com). Settings: WhatsApp:Token, WhatsApp:PhoneNumberId,
    /// optional WhatsApp:GraphVersion (default v23.0) and WhatsApp:TemplateLanguage (default en_US).
    /// </summary>
    public class WhatsAppCloudClient : IWhatsAppClient
    {
        private readonly IHttpClientFactory _http;
        private readonly IConfiguration _config;
        private readonly ILogger<WhatsAppCloudClient> _logger;

        public WhatsAppCloudClient(IHttpClientFactory http, IConfiguration config, ILogger<WhatsAppCloudClient> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;
        }

        private string? Token => _config["WhatsApp:Token"];
        private string? PhoneNumberId => _config["WhatsApp:PhoneNumberId"];
        public bool IsConfigured => !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(PhoneNumberId);

        public Task<WhatsAppSendResult> SendTextAsync(string to, string text) =>
            PostAsync(new { messaging_product = "whatsapp", to, type = "text", text = new { body = text } });

        public async Task<WhatsAppSendResult> SendTemplateAsync(string to, string templateName, IReadOnlyList<string> bodyParameters)
        {
            var primary = string.IsNullOrWhiteSpace(_config["WhatsApp:TemplateLanguage"]) ? "en_US" : _config["WhatsApp:TemplateLanguage"]!;
            WhatsAppSendResult result = WhatsAppSendResult.Fail("not sent");
            // The template may be registered under another English code: try the usual ones.
            foreach (var lang in new[] { primary, "en", "en_GB", "en_US" }.Distinct())
            {
                result = await PostAsync(new
                {
                    messaging_product = "whatsapp",
                    to,
                    type = "template",
                    template = new
                    {
                        name = templateName,
                        language = new { code = lang },
                        components = new[]
                        {
                            new { type = "body", parameters = bodyParameters.Select(p => new { type = "text", text = p }).ToArray() }
                        }
                    }
                });
                if (result.Ok) break;
            }
            return result;
        }

        public Task<WhatsAppSendResult> SendLocationAsync(string to, decimal latitude, decimal longitude, string name, string address) =>
            PostAsync(new
            {
                messaging_product = "whatsapp",
                to,
                type = "location",
                location = new
                {
                    latitude = latitude.ToString(CultureInfo.InvariantCulture),
                    longitude = longitude.ToString(CultureInfo.InvariantCulture),
                    name,
                    address
                }
            });

        private async Task<WhatsAppSendResult> PostAsync(object payload)
        {
            if (!IsConfigured) return WhatsAppSendResult.Fail("WhatsApp is not configured (WhatsApp:Token / WhatsApp:PhoneNumberId).");
            var version = string.IsNullOrWhiteSpace(_config["WhatsApp:GraphVersion"]) ? "v23.0" : _config["WhatsApp:GraphVersion"];
            try
            {
                var client = _http.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(20);
                using var req = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{version}/{PhoneNumberId}/messages")
                {
                    Content = JsonContent.Create(payload)
                };
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
                using var res = await client.SendAsync(req);
                var body = await res.Content.ReadAsStringAsync();
                using var doc = string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body);
                if (res.IsSuccessStatusCode)
                {
                    string? id = null;
                    if (doc != null && doc.RootElement.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array && msgs.GetArrayLength() > 0
                        && msgs[0].TryGetProperty("id", out var mid))
                        id = mid.GetString();
                    return new WhatsAppSendResult(true, id, null, null);
                }
                string? error = null; int? code = null;
                if (doc != null && doc.RootElement.TryGetProperty("error", out var err))
                {
                    if (err.TryGetProperty("message", out var m)) error = m.GetString();
                    if (err.TryGetProperty("code", out var cd) && cd.TryGetInt32(out var ci)) code = ci;
                }
                _logger.LogWarning("WhatsApp API {Status}: {Code} {Error}", (int)res.StatusCode, code, error);
                return WhatsAppSendResult.Fail(error ?? $"WhatsApp API returned {(int)res.StatusCode}", code);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WhatsApp API call failed");
                return WhatsAppSendResult.Fail("WhatsApp could not be reached.");
            }
        }
    }
}
