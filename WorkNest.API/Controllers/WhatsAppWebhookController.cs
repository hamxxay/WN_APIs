using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// WhatsApp Cloud API webhook (set in Meta's app dashboard as https://&lt;api host&gt;/api/whatsapp/webhook).
    /// GET: Meta's verification (WhatsApp:VerifyToken). POST: incoming messages, answered by the WorkNest bot.
    /// When WhatsApp:AppSecret is set, every POST must carry Meta's X-Hub-Signature-256.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [DisableRateLimiting]
    public class WhatsAppWebhookController : ControllerBase
    {
        private readonly IWhatsAppBotService _bot;
        private readonly IConfiguration _config;
        private readonly ILogger<WhatsAppWebhookController> _logger;

        public WhatsAppWebhookController(IWhatsAppBotService bot, IConfiguration config, ILogger<WhatsAppWebhookController> logger)
        {
            _bot = bot;
            _config = config;
            _logger = logger;
        }

        [HttpGet("api/whatsapp/webhook")]
        public IActionResult Verify([FromQuery(Name = "hub.mode")] string? mode,
            [FromQuery(Name = "hub.verify_token")] string? token, [FromQuery(Name = "hub.challenge")] string? challenge)
        {
            var expected = _config["WhatsApp:VerifyToken"];
            if (mode == "subscribe" && !string.IsNullOrWhiteSpace(expected) && token == expected)
                return Content(challenge ?? "", "text/plain");
            return StatusCode(403);
        }

        [HttpPost("api/whatsapp/webhook")]
        public async Task<IActionResult> Receive()
        {
            Request.EnableBuffering();
            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true)) body = await reader.ReadToEndAsync();

            if (!SignatureIsValid(body))
            {
                _logger.LogWarning("WhatsApp webhook rejected: invalid signature");
                return Unauthorized();
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array) return Ok();
                foreach (var entry in entries.EnumerateArray())
                {
                    if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
                    foreach (var change in changes.EnumerateArray())
                    {
                        if (!change.TryGetProperty("value", out var value)) continue;
                        if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array) continue; // delivery statuses etc.

                        string? profileName = null;
                        if (value.TryGetProperty("contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array && contacts.GetArrayLength() > 0
                            && contacts[0].TryGetProperty("profile", out var profile) && profile.TryGetProperty("name", out var pn))
                            profileName = pn.GetString();

                        foreach (var m in messages.EnumerateArray())
                        {
                            var from = m.TryGetProperty("from", out var f) ? f.GetString() : null;
                            if (string.IsNullOrWhiteSpace(from)) continue;
                            var id = m.TryGetProperty("id", out var mid) ? mid.GetString() : null;
                            var type = m.TryGetProperty("type", out var t) ? t.GetString() ?? "unknown" : "unknown";
                            string? text = null;
                            if (type == "text" && m.TryGetProperty("text", out var tx) && tx.TryGetProperty("body", out var b)) text = b.GetString();
                            try
                            {
                                await _bot.HandleIncomingAsync(from, profileName, id, type, text);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "WhatsApp bot failed for message {MessageId}", id);
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "WhatsApp webhook: body is not JSON");
            }
            return Ok(new { status = "received" }); // always 200, or Meta keeps re-sending
        }

        private bool SignatureIsValid(string body)
        {
            var secret = _config["WhatsApp:AppSecret"];
            if (string.IsNullOrWhiteSpace(secret)) return true; // not configured: accepted (set it in production)
            var header = Request.Headers["X-Hub-Signature-256"].ToString();
            if (!header.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) return false;
            var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(header[7..].ToLowerInvariant()));
        }
    }
}
