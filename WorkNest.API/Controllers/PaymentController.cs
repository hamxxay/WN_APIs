using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Payment;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentService _payments;
        public PaymentController(IPaymentService payments) => _payments = payments;

        [HttpGet("api/payment/my")]
        public async Task<IActionResult> MyPayments([FromHeader(Name = "x-user-email")] string? userEmail)
        {
            if (string.IsNullOrWhiteSpace(userEmail))
                return Unauthorized(new { isSuccessful = false, message = "User email header required" });
            return Ok(await _payments.GetMyPaymentsAsync(userEmail));
        }

        [HttpGet("api/payment")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string search = "")
        {
            var all = await _payments.GetAllPaymentsAsync();
            var (items, total) = PaginationHelper.Paginate(all, page, limit, search);
            return Ok(new PaginatedResponse<object> { Data = items, Total = total });
        }

        [HttpGet("api/payment/{id}/summary")]
        public async Task<IActionResult> Summary(string id)
        {
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

        [HttpPatch("api/payment/{id}/status")]
        public async Task<IActionResult> UpdateStatus(
            string id,
            [FromQuery] string status,
            [FromQuery] string? transactionRef) =>
            Ok(await _payments.UpdatePaymentStatusAsync(id, status, transactionRef));

        [HttpPost("api/payment/{id}/approve")]
        public async Task<IActionResult> Approve(string id)
        {
            var result = await _payments.ApprovePaymentAsync(id);
            if (!result.IsSuccessful) return NotFound(result);
            return Ok(result);
        }

        [HttpDelete("api/payment/{id}")]
        public async Task<IActionResult> Delete(string id) =>
            Ok(await _payments.DeletePaymentAsync(id));

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
