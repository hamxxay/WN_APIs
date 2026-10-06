using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Payment;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentService _payments;
        private readonly IDbRepository _db;
        public PaymentController(IPaymentService payments, IDbRepository db) { _payments = payments; _db = db; }

        // Prefer the email from the JWT; fall back to the x-user-email header (same as BookingController).
        private string? ResolveUserEmail(string? headerEmail)
        {
            var claimEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                             ?? User.FindFirst("email")?.Value;
            return !string.IsNullOrWhiteSpace(claimEmail) ? claimEmail : headerEmail;
        }

        [HttpGet("api/payment/my")]
        public async Task<IActionResult> MyPayments([FromHeader(Name = "x-user-email")] string? userEmail)
        {
            var email = ResolveUserEmail(userEmail);
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { isSuccessful = false, message = "User identity or email header required" });
            return Ok(await _payments.GetMyPaymentsAsync(email));
        }

        [HttpGet("api/payment")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null)
        {
            var (items, total) = await _payments.GetPaymentsAsync(page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpGet("api/payment/{id:int}/summary")]
        public async Task<IActionResult> Summary(int id)
        {
            var result = await _payments.GetPaymentSummaryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpGet("api/payment/{publicId:guid}/summary")]
        public async Task<IActionResult> SummaryByGuid(Guid publicId)
        {
            var (rows, _) = await _db.GetPaymentsAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Payment not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            var result = await _payments.GetPaymentSummaryAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/payment")]
        public async Task<IActionResult> Create(
            [FromBody] PaymentCreateRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "User email header required" });
            return StatusCode(201, await _payments.CreatePaymentAsync(request, userEmail));
        }

        [HttpPatch("api/payment/{id:int}/status")]
        public async Task<IActionResult> UpdateStatus(
            int id,
            [FromBody] PaymentStatusUpdateRequest request) =>
            Ok(await _payments.UpdatePaymentStatusAsync(id, request.StatusId, null));

        [HttpPatch("api/payment/{publicId:guid}/status")]
        public async Task<IActionResult> UpdateStatusByGuid(Guid publicId, [FromBody] PaymentStatusUpdateRequest request)
        {
            var (rows, _) = await _db.GetPaymentsAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Payment not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _payments.UpdatePaymentStatusAsync(id, request.StatusId, null));
        }

        // Approve a pending payment (sets status to Paid)
        [HttpPost("api/payment/{id:int}/approve")]
        public async Task<IActionResult> Approve(int id)
        {
            // StatusId 2 = Paid — adjust to match your WN_PaymentStatuses lookup
            return Ok(await _payments.UpdatePaymentStatusAsync(id, 2, null));
        }

        [HttpPost("api/payment/{publicId:guid}/approve")]
        public async Task<IActionResult> ApproveByGuid(Guid publicId)
        {
            var (rows, _) = await _db.GetPaymentsAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Payment not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _payments.UpdatePaymentStatusAsync(id, 2, null));
        }

        [HttpDelete("api/payment/{id:int}")]
        public async Task<IActionResult> Delete(int id) =>
            Ok(await _payments.DeletePaymentAsync(id));

        [HttpDelete("api/payment/{publicId:guid}")]
        public async Task<IActionResult> DeleteByGuid(Guid publicId)
        {
            var (rows, _) = await _db.GetPaymentsAsync(1, 10000, null);
            var match = rows.FirstOrDefault(r => r.TryGetValue("PublicId", out var g) && g?.ToString() == publicId.ToString());
            if (match is null) return NotFound(ApiResponse.Fail("Payment not found"));
            var id = match.TryGetValue("Id", out var rid) ? Convert.ToInt32(rid) : 0;
            return Ok(await _payments.DeletePaymentAsync(id));
        }

        [HttpPost("api/payment/card")]
        public async Task<IActionResult> Card(
            [FromBody] CardPaymentRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "User email header required" });
            var result = await _payments.ProcessCardPaymentAsync(request, userEmail);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/payment/voucher/generate")]
        public async Task<IActionResult> GenerateVoucher(
            [FromBody] VoucherGenerateRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "User email header required" });
            var result = await _payments.GenerateVoucherAsync(request, userEmail);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpPost("api/payment/payfast/initiate")]
        public async Task<IActionResult> PayFastInitiate(
            [FromBody] PayFastInitiateRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "User email header required" });
            var result = await _payments.InitiatePayFastAsync(request, userEmail);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

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
    }
}
