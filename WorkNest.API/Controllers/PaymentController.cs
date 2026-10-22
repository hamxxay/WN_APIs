using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Lists;
using WorkNest.Application.DTOs.Payment;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.API.Filters;
using WorkNest.Common.Responses;
using WorkNest.API.Extensions;
using Microsoft.AspNetCore.RateLimiting;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    [RecordScope(RecordKind.Payment, "id", "publicId")] // location-bound staff: only records of their locations
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentService _payments;
        private readonly IDbRepository _db;
        private readonly IRecordScopeRepository _scope;
        private readonly IOneBillService _oneBill;
        private readonly IConfiguration _config;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            IPaymentService payments,
            IDbRepository db,
            IRecordScopeRepository scope,
            IOneBillService oneBill,
            IConfiguration config,
            ILogger<PaymentController> logger)
        {
            _payments = payments;
            _db = db;
            _scope = scope;
            _oneBill = oneBill;
            _config = config;
            _logger = logger;
        }

        // Staff = admin / super admin / sales executive / receptionist; customers (role "general") are not staff.
        private const string StaffRoles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive";
        private const string AdminRoles = "admin,Admin,super_admin,SuperAdmin";

        /// <summary>
        /// Identity comes only from the JWT email claim (the x-user-email header is no longer trusted).
        /// Customers may only pay for their own booking; staff may act on any booking.
        /// </summary>
        private async Task<(string? Email, IActionResult? Error)> ResolveCallerForBookingAsync(int? bookingId)
        {
            var email = User.GetEmail();
            if (string.IsNullOrWhiteSpace(email))
                return (null, Unauthorized(new { isSuccessful = false, message = "Sign in required." }));
            if (bookingId is > 0 && !User.IsStaff() && !await _db.IsBookingOwnedByDbAsync(bookingId.Value, email))
                return (null, NotFound(new { isSuccessful = false, message = "Booking not found." }));
            return (email, null);
        }

        [HttpGet("api/payment/my")]
        public async Task<IActionResult> MyPayments()
        {
            var userEmail = User.GetEmail();
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "Sign in required." });
            return Ok(await _payments.GetMyPaymentsAsync(userEmail));
        }

        // locationId, status (a payment StatusId from api/payment/statuses), from / to (payment date = paid on, else
        // created on; business dates, both included): only then the LST_01 procedure is used (location-bound staff stay
        // limited to their locations); without them the list is read exactly as before.
        [HttpGet("api/payment")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> List(
            [FromServices] IListFilterService lists,
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null,
            [FromQuery] int? locationId = null,
            [FromQuery] string? status = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 100);
            var filter = new ListFilter { Status = status, From = from, To = to };
            if (filter.HasStatusOrDates || locationId is > 0)
                return await ListFilterResults.RunAsync(async () =>
                {
                    var (rows, count) = await lists.GetPaymentsAsync(page, limit, search, User.ScopedLocationIds(locationId), filter);
                    return Ok(new PaginatedResponse<object> { Data = rows, Total = count });
                });
            if (!User.IsLocationBoundRole())
            {
                var (items, total) = await _payments.GetPaymentsAsync(page, limit, search);
                return Ok(new PaginatedResponse<object> { Data = items, Total = total });
            }
            // Admins / sales executives: only payments of their locations (the list procedure has no location filter).
            var allowed = await _scope.GetPaymentIdsInLocationsAsync(User.GetLocationIds());
            var (all, _) = await _payments.GetPaymentsAsync(1, 5000, search);
            var mine = all.Where(r => r is IDictionary<string, object?> d && d.TryGetValue("Id", out var id) && id != null && allowed.Contains(Convert.ToInt32(id))).ToList();
            return Ok(new PaginatedResponse<object> { Data = mine.Skip((page - 1) * limit).Take(limit), Total = mine.Count });
        }

        /// <summary>Choices of the Payments list Status filter (the statuses the payments have), [{ value, label }].</summary>
        [HttpGet("api/payment/statuses")]
        [Authorize(Roles = StaffRoles)]
        public Task<IActionResult> ListStatuses([FromServices] IListFilterService lists) =>
            ListFilterResults.StatusOptionsAsync(lists, ListFilterRules.Payments);

        [HttpGet("api/payment/{id:int}/summary")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> Summary(int id)
        {
            var result = await _payments.GetPaymentSummaryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("api/payment/{publicId:guid}/summary")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> SummaryByGuid(Guid publicId)
        {
            var id = await _scope.GetPaymentIdByPublicIdAsync(publicId) ?? 0;
            if (id == 0) return NotFound(ApiResponse.Fail("Payment not found"));
            var result = await _payments.GetPaymentSummaryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/payment")]
        public async Task<IActionResult> Create([FromBody] PaymentCreateRequest request)
        {
            var (userEmail, error) = await ResolveCallerForBookingAsync(request?.BookingId);
            if (error != null) return error;
            return StatusCode(201, await _payments.CreatePaymentAsync(request!, userEmail!));
        }

        [HttpPatch("api/payment/{id:int}/status")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> UpdateStatus(
            int id,
            [FromBody] PaymentStatusUpdateRequest request) =>
            Ok(await _payments.UpdatePaymentStatusAsync(id, request.StatusId, null));

        [HttpPatch("api/payment/{publicId:guid}/status")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> UpdateStatusByGuid(Guid publicId, [FromBody] PaymentStatusUpdateRequest request)
        {
            var id = await _scope.GetPaymentIdByPublicIdAsync(publicId) ?? 0;
            if (id == 0) return NotFound(ApiResponse.Fail("Payment not found"));
            return Ok(await _payments.UpdatePaymentStatusAsync(id, request.StatusId, null));
        }

        // Approve a pending payment (sets status to Paid)
        [HttpPost("api/payment/{id:int}/approve")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> Approve(int id)
        {
            // StatusId 2 = Paid — adjust to match your WN_PaymentStatuses lookup
            return Ok(await _payments.UpdatePaymentStatusAsync(id, 2, null));
        }

        [HttpPost("api/payment/{publicId:guid}/approve")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> ApproveByGuid(Guid publicId)
        {
            var id = await _scope.GetPaymentIdByPublicIdAsync(publicId) ?? 0;
            if (id == 0) return NotFound(ApiResponse.Fail("Payment not found"));
            return Ok(await _payments.UpdatePaymentStatusAsync(id, 2, null));
        }

        [HttpDelete("api/payment/{id:int}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _payments.DeletePaymentAsync(id));

        [HttpDelete("api/payment/{publicId:guid}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> DeleteByGuid(Guid publicId)
        {
            var id = await _scope.GetPaymentIdByPublicIdAsync(publicId) ?? 0;
            if (id == 0) return NotFound(ApiResponse.Fail("Payment not found"));
            return Ok(await _payments.DeletePaymentAsync(id));
        }

        [HttpPost("api/payment/card")]
        public async Task<IActionResult> Card([FromBody] CardPaymentRequest request)
        {
            var (userEmail, error) = await ResolveCallerForBookingAsync(request?.BookingId);
            if (error != null) return error;
            var result = await _payments.ProcessCardPaymentAsync(request!, userEmail!);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/payment/voucher/generate")]
        public async Task<IActionResult> GenerateVoucher([FromBody] VoucherGenerateRequest request)
        {
            var (userEmail, error) = await ResolveCallerForBookingAsync(request?.BookingId);
            if (error != null) return error;
            var result = await _payments.GenerateVoucherAsync(request!, userEmail!);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/payment/payfast/initiate")]
        public async Task<IActionResult> PayFastInitiate([FromBody] PayFastInitiateRequest request)
        {
            var (userEmail, error) = await ResolveCallerForBookingAsync(request?.BookingId);
            if (error != null) return error;
            var result = await _payments.InitiatePayFastAsync(request!, userEmail!);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [DisableRateLimiting] // machine-to-machine caller: never throttle
        [HttpPost("api/payment/payfast/notify")]
        [AllowAnonymous]
        public async Task<IActionResult> PayFastNotify()
        {
            var form = await Request.ReadFormAsync();
            var data = form.ToDictionary(k => k.Key, v => v.Value.ToString());
            var result = await _payments.HandlePayFastNotifyAsync(data);
            if (!result.IsSuccessful) return BadRequest(result);
            return Ok(result);
        }

        /// <summary>
        /// EzPay payment notifications (EzPay vendor portal, Notification Configuration): when a customer pays a One Bill
        /// invoice, EzPay posts { billno, rcvdamount, rcvddate, rcvdtime, rcvdvia } with the header api_key.
        /// Authenticated via API key configured in appsettings.json under OneBill:ApiKey or OneBill:NotifyApiKey.
        /// Answers HTTP 200 { "status": "success" } once it is safely stored.
        /// </summary>
        [DisableRateLimiting] // machine-to-machine caller: never throttle
        [HttpPost("api/onebill/payment-notification")]
        [AllowAnonymous]
        public async Task<IActionResult> OneBillPaymentNotification(
            [FromHeader(Name = "api_key")] string? apiKey = null,
            [FromQuery(Name = "api_key")] string? queryApiKey = null,
            CancellationToken ct = default)
        {
            var expectedKey = _config["OneBill:NotifyApiKey"] ?? _config["OneBill:ApiKey"];
            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                _logger.LogError("One Bill notification refused: OneBill:ApiKey or OneBill:NotifyApiKey is not set in configuration.");
                return StatusCode(503, OneBillError("Notifications are not set up."));
            }
            var sentKey = apiKey
                          ?? queryApiKey
                          ?? Request.Headers["api_key"].FirstOrDefault()
                          ?? Request.Headers["api-key"].FirstOrDefault()
                          ?? Request.Headers["x-api-key"].FirstOrDefault()
                          ?? Request.Headers["X-API-KEY"].FirstOrDefault()
                          ?? Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (!SameOneBillKey(sentKey, expectedKey))
            {
                _logger.LogWarning("One Bill notification refused: wrong or missing api_key (from {Ip}).", RemoteIp());
                return Unauthorized(OneBillError("Invalid api_key."));
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
                return BadRequest(OneBillError("The body must be JSON."));
            }
            if (body.ValueKind != JsonValueKind.Object)
                return BadRequest(OneBillError("The body must be a JSON object."));

            var billNo = OneBillText(body, "billno");
            var amount = OneBillNumber(body, "rcvdamount");
            if (string.IsNullOrWhiteSpace(billNo) || billNo.Length > 30)
                return BadRequest(OneBillError("billno is required."));
            if (amount is null)
                return BadRequest(OneBillError("rcvdamount is required."));

            var notice = new OneBillPaymentNotice
            {
                BillNo = billNo,
                Amount = amount.Value,
                ReceivedDate = DateTime.TryParseExact(OneBillText(body, "rcvddate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                ReceivedTime = TimeSpan.TryParseExact(OneBillText(body, "rcvdtime"), new[] { @"hh\:mm\:ss", @"hh\:mm" }, CultureInfo.InvariantCulture, out var t) ? t : null,
                ReceivedVia = OneBillText(body, "rcvdvia"),
                RawBody = raw.Length > 4000 ? raw[..4000] : raw,
                RemoteIp = RemoteIp(),
            };

            try
            {
                var result = await _oneBill.RecordPaymentAsync(notice, ct);
                _logger.LogInformation("One Bill notification #{Id}: bill {BillNo}, {Amount} via {Via}: {Outcome}. {Message}",
                    result.NotificationId, notice.BillNo, notice.Amount, notice.ReceivedVia, result.Outcome, result.Message);
                if (result.Outcome == "Invalid")
                    return BadRequest(OneBillError(result.Message ?? "Invalid notification."));
                return Ok(new { status = "success" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "One Bill notification for bill {BillNo} ({Amount}) could not be recorded.", notice.BillNo, notice.Amount);
                return StatusCode(500, OneBillError("The notification could not be recorded. Please send it again."));
            }
        }

        private static object OneBillError(string message) => new { status = "error", message };

        private static bool SameOneBillKey(string? sent, string expected)
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
        private static string? OneBillText(JsonElement body, string name)
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

        private static decimal? OneBillNumber(JsonElement body, string name) =>
            decimal.TryParse(OneBillText(body, name)?.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
