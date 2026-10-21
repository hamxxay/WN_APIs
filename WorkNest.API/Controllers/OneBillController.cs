using System.Globalization;
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
    /// EzPay payment notifications (EzPay vendor portal, Notification Configuration): when a customer pays a One Bill
    /// invoice, EzPay posts { billno, rcvdamount, rcvddate, rcvdtime, rcvdvia } with the header api_key. The notification
    /// is kept in dbo.WN_OneBill_Payments and recorded on the invoice (script OB_03_one_bill_payments).
    /// Answers HTTP 200 { "status": "success" } once it is safely stored (also for a repeat, an already paid invoice or an
    /// unknown bill number, which are kept for review), so EzPay stops sending it. Any other answer makes EzPay send again.
    /// Settings: OneBill:NotifyApiKey (the api_key EzPay sends).
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    public class OneBillController : ControllerBase
    {
        private readonly IOneBillService _oneBill;
        private readonly IConfiguration _config;
        private readonly ILogger<OneBillController> _logger;

        public OneBillController(IOneBillService oneBill, IConfiguration config, ILogger<OneBillController> logger)
        {
            _oneBill = oneBill;
            _config = config;
            _logger = logger;
        }

        [DisableRateLimiting] // machine-to-machine caller: never throttle
        [HttpPost("api/onebill/payment-notification")]
        public async Task<IActionResult> PaymentNotification(CancellationToken ct)
        {
            var expectedKey = _config["OneBill:NotifyApiKey"];
            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                _logger.LogError("One Bill notification refused: OneBill:NotifyApiKey is not set.");
                return StatusCode(503, Error("Notifications are not set up."));
            }
            var sentKey = Request.Headers["api_key"].FirstOrDefault()
                          ?? Request.Headers["api-key"].FirstOrDefault()
                          ?? Request.Headers["x-api-key"].FirstOrDefault();
            if (!SameKey(sentKey, expectedKey))
            {
                _logger.LogWarning("One Bill notification refused: wrong or missing api_key (from {Ip}).", RemoteIp());
                return Unauthorized(Error("Invalid api_key."));
            }

            string raw;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
                raw = await reader.ReadToEndAsync(ct);

            JsonElement body;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
                body = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return BadRequest(Error("The body must be JSON."));
            }
            if (body.ValueKind != JsonValueKind.Object)
                return BadRequest(Error("The body must be a JSON object."));

            var billNo = Text(body, "billno");
            var amount = Number(body, "rcvdamount");
            if (string.IsNullOrWhiteSpace(billNo) || billNo.Length > 30)
                return BadRequest(Error("billno is required."));
            if (amount is null)
                return BadRequest(Error("rcvdamount is required."));

            var notice = new OneBillPaymentNotice
            {
                BillNo = billNo,
                Amount = amount.Value,
                ReceivedDate = DateTime.TryParseExact(Text(body, "rcvddate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                ReceivedTime = TimeSpan.TryParseExact(Text(body, "rcvdtime"), new[] { @"hh\:mm\:ss", @"hh\:mm" }, CultureInfo.InvariantCulture, out var t) ? t : null,
                ReceivedVia = Text(body, "rcvdvia"),
                RawBody = raw.Length > 4000 ? raw[..4000] : raw,
                RemoteIp = RemoteIp(),
            };

            try
            {
                var result = await _oneBill.RecordPaymentAsync(notice, ct);
                _logger.LogInformation("One Bill notification #{Id}: bill {BillNo}, {Amount} via {Via}: {Outcome}. {Message}",
                    result.NotificationId, notice.BillNo, notice.Amount, notice.ReceivedVia, result.Outcome, result.Message);
                if (result.Outcome == "Invalid")
                    return BadRequest(Error(result.Message ?? "Invalid notification."));
                return Ok(new { status = "success" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "One Bill notification for bill {BillNo} ({Amount}) could not be recorded.", notice.BillNo, notice.Amount);
                return StatusCode(500, Error("The notification could not be recorded. Please send it again."));
            }
        }

        private static object Error(string message) => new { status = "error", message };

        private static bool SameKey(string? sent, string expected)
        {
            if (string.IsNullOrEmpty(sent)) return false;
            // Constant-time compare, so the answer time does not reveal how much of the key matched.
            var a = SHA256.HashData(Encoding.UTF8.GetBytes(sent.Trim()));
            var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected.Trim()));
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        private string? RemoteIp() =>
            Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim() ?? HttpContext.Connection.RemoteIpAddress?.ToString();

        /// <summary>A property as text, whatever its JSON type ("123" or 123); null when missing or empty.</summary>
        private static string? Text(JsonElement body, string name)
        {
            foreach (var p in body.EnumerateObject())
            {
                if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                var v = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number => p.Value.GetRawText(),
                    _ => null
                };
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }
            return null;
        }

        private static decimal? Number(JsonElement body, string name) =>
            decimal.TryParse(Text(body, name)?.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
