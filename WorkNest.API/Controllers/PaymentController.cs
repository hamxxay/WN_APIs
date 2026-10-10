using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Payment;
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
        public PaymentController(IPaymentService payments, IDbRepository db, IRecordScopeRepository scope)
        {
            _payments = payments;
            _db = db;
            _scope = scope;
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

        [HttpGet("api/payment")]
        [Authorize(Roles = StaffRoles)]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null)
        {
            page = Math.Max(1, page);
            limit = Math.Clamp(limit, 1, 100);
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
    }
}
