using WorkNest.Application.DTOs.Payment;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly IDbRepository _db;
        private readonly IPayFastService _payFast;

        public PaymentService(IDbRepository db, IPayFastService payFast)
        {
            _db = db;
            _payFast = payFast;
        }

        public async Task<(IEnumerable<object> Items, int Total)> GetPaymentsAsync(int page, int limit, string? search)
        {
            var (rows, total) = await _db.GetPaymentsAsync(page, limit, search);
            return (rows.Cast<object>(), total);
        }

        public async Task<IEnumerable<object>> GetMyPaymentsAsync(string userEmail) =>
            (await _db.GetMyPaymentsAsync(userEmail)).Cast<object>();

        public async Task<ApiResponse> GetPaymentSummaryAsync(int id)
        {
            var payment = await _db.GetPaymentSummaryAsync(id);
            if (payment is null) return ApiResponse.Fail("Payment not found");
            return ApiResponse.Ok(payment);
        }

        public async Task<ApiResponse> CreatePaymentAsync(PaymentCreateRequest request, string userEmail)
        {
            var userRow = await _db.GetUserByEmailAsync(userEmail);
            if (userRow is null) return ApiResponse.Fail("User not found");
            var userId = Convert.ToInt32(userRow["Id"]);

            var result = await _db.InsertPaymentAsync(userId, request.BookingId,
                request.PaymentMethodId, request.Amount, request.Notes, userId);
            return ApiResponse.Ok(result, "Payment created.");
        }

        public async Task<ApiResponse> GenerateVoucherAsync(VoucherGenerateRequest request, string userEmail)
        {
            var userRow = await _db.GetUserByEmailAsync(userEmail);
            if (userRow is null) return ApiResponse.Fail("User not found");
            var userId = Convert.ToInt32(userRow["Id"]);

            var result = await _db.GenerateVoucherAsync(userId, request.BookingId,
                request.Amount, request.ExpiresOn, userId);
            return ApiResponse.Ok(result, "Voucher generated.");
        }

        public async Task<ApiResponse> UpdatePaymentStatusAsync(int id, byte statusId, int? actorId)
        {
            await _db.UpdatePaymentStatusAsync(id, statusId, actorId);
            return ApiResponse.Ok("Payment status updated.");
        }

        public async Task<ApiResponse> DeletePaymentAsync(int id)
        {
            await _db.DeletePaymentAsync(id);
            return ApiResponse.Ok("Payment deleted.");
        }

        public async Task<ApiResponse> ProcessCardPaymentAsync(CardPaymentRequest request, string userEmail)
        {
            var userRow = await _db.GetUserByEmailAsync(userEmail);
            if (userRow is null) return ApiResponse.Fail("User not found");
            var userId = Convert.ToInt32(userRow["Id"]);

            // PaymentMethodId 2 = Card (adjust to match your lookup table)
            var result = await _db.InsertPaymentAsync(userId, request.BookingId, 2, 0, null, userId);
            return ApiResponse.Ok(result, "Card payment processed.");
        }

        public async Task<ApiResponse> InitiatePayFastAsync(PayFastInitiateRequest request, string userEmail)
        {
            var userRow = await _db.GetUserByEmailAsync(userEmail);
            if (userRow is null) return ApiResponse.Fail("User not found");
            var userId = Convert.ToInt32(userRow["Id"]);

            var orderId = $"WN-{request.BookingId}-{Random.Shared.Next(100000, 999999)}";
            var payload = _payFast.BuildPayload(request.BookingId.ToString(), 0,
                $"WorkNest Booking #{request.BookingId}",
                request.CustomerEmail, request.CustomerName, orderId);

            // PaymentMethodId 3 = PayFast (adjust to match your lookup table)
            await _db.InsertPaymentAsync(userId, request.BookingId, 3, 0, orderId, userId);
            return ApiResponse.Ok(payload, "PayFast payment initiated.");
        }

        public async Task<ApiResponse> HandlePayFastNotifyAsync(Dictionary<string, string> formData)
        {
            if (!_payFast.VerifySignature(new Dictionary<string, string>(formData)))
                return ApiResponse.Fail("Invalid PayFast signature");

            // PayFast notify is stateless — status update handled externally via UpdatePaymentStatus
            return ApiResponse.Ok();
        }
    }
}
