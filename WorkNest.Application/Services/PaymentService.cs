using WorkNest.Application.DTOs.Payment;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly IDbRepository _db;
        private readonly IPayFastService _payFast;

        public PaymentService(IDbRepository db, IPayFastService payFast)
        {
            _db      = db;
            _payFast = payFast;
        }

        public async Task<IEnumerable<object>> GetAllPaymentsAsync() =>
            (await _db.GetAllPaymentsAsync()).Select(r => (object)new
            {
                id             = r.TryGetValue("Id",             out var i)  ? i  : null,
                idGuid         = r.TryGetValue("IdGuid",         out var g)  ? g?.ToString()  : null,
                userEmail      = r.TryGetValue("UserEmail",      out var ue) ? ue?.ToString() : null,
                amount         = r.TryGetValue("Amount",         out var a)  ? a  : null,
                paymentMethod  = r.TryGetValue("PaymentMethod",  out var pm) ? pm?.ToString() : null,
                paymentStatus  = r.TryGetValue("PaymentStatus",  out var ps) ? ps?.ToString() : null,
                transactionRef = r.TryGetValue("TransactionRef", out var tr) ? tr?.ToString() : null,
                paidAt         = r.TryGetValue("PaidAt",         out var pa) ? pa?.ToString() : null,
            });

        public async Task<IEnumerable<object>> GetMyPaymentsAsync(string userEmail)
        {
            var (_, userGuid) = await _db.GetUserIdByEmailAsync(userEmail);
            if (userGuid is null) return [];
            return (await _db.GetMyPaymentsAsync(userGuid)).Cast<object>();
        }

        public async Task<ApiResponse> GetPaymentSummaryAsync(string id)
        {
            var payments = await _db.GetAllPaymentsAsync();
            var payment  = payments.FirstOrDefault(p =>
                (p.TryGetValue("idGuid", out var g) ? g?.ToString() : null) == id ||
                (p.TryGetValue("id",     out var i) ? i?.ToString() : null) == id);

            if (payment is null) return ApiResponse.Fail("Payment not found");

            // id here is a payment GUID — get user GUID from the payment row to fetch their payments
            var userGuid     = payment.TryGetValue("userId", out var u) ? u?.ToString() : null;
            var userPayments = userGuid is not null
                ? (await _db.GetPaymentsByUserGuidAsync(userGuid)).ToList()
                : new List<IDictionary<string, object?>>();

            var paidTotal = userPayments
                .Where(p => p.TryGetValue("paymentStatus", out var s) && s?.ToString() == "Paid")
                .Sum(p => p.TryGetValue("amount", out var a) ? Convert.ToDouble(a) : 0);

            return ApiResponse.Ok(new
            {
                payment,
                booking    = (object?)null,
                membership = (object?)null,
                userPaymentStats = new
                {
                    totalPayments    = userPayments.Count,
                    paidPayments     = userPayments.Count(p => p.TryGetValue("paymentStatus", out var s) && s?.ToString() == "Paid"),
                    pendingPayments  = userPayments.Count(p => p.TryGetValue("paymentStatus", out var s) && s?.ToString() == "Pending"),
                    failedPayments   = userPayments.Count(p => p.TryGetValue("paymentStatus", out var s) && s?.ToString() == "Failed"),
                    refundedPayments = userPayments.Count(p => p.TryGetValue("paymentStatus", out var s) && s?.ToString() == "Refunded"),
                    totalPaidAmount  = paidTotal,
                },
                recentUserPayments = userPayments.Take(5),
            });
        }

        public async Task<ApiResponse> CreatePaymentAsync(PaymentCreateRequest request, string userEmail)
        {
            var (_, userGuid) = await _db.GetUserIdByEmailAsync(userEmail);
            if (userGuid is null) return ApiResponse.Fail("User not found");

            var txRef = GuidHelper.GenerateRef("ADM");
            // Membership-based payment — no booking GUID; pass empty string as bookingGuid placeholder
            await _db.CreatePaymentAsync(userGuid, string.Empty,
                request.Amount, request.PaymentMethod, txRef);
            return ApiResponse.Ok("Payment created.");
        }

        public async Task<ApiResponse> UpdatePaymentStatusAsync(string id, string status, string? transactionRef)
        {
            if (!string.IsNullOrWhiteSpace(transactionRef))
                await _db.UpdatePaymentStatusByRefAsync(transactionRef, status);
            else
                await _db.UpdatePaymentStatusByGuidAsync(id, status);
            return ApiResponse.Ok("Payment status updated.");
        }

        public async Task<ApiResponse> DeletePaymentAsync(string id)
        {
            await _db.SoftDeletePaymentAsync(id);
            return ApiResponse.Ok("Payment deleted.");
        }

        public async Task<ApiResponse> ProcessCardPaymentAsync(CardPaymentRequest request, string userEmail)
        {
            var (_, userGuid) = await _db.GetUserIdByEmailAsync(userEmail);
            if (userGuid is null) return ApiResponse.Fail("User not found");

            var booking = await _db.GetBookingByGuidAsync(request.BookingId);
            if (booking is null) return ApiResponse.Fail("Booking not found");

            var amount = booking.TryGetValue("totalAmount", out var a) ? Convert.ToDouble(a) : 0;
            var txRef  = $"TXN-CARD-{request.BookingId}-{Random.Shared.Next(100000, 999999)}";
            await _db.CreatePaymentAsync(userGuid, request.BookingId, amount, "Card", txRef);
            return ApiResponse.Ok(new { transactionRef = txRef }, "Card payment processed.");
        }

        public async Task<ApiResponse> GenerateVoucherAsync(VoucherGenerateRequest request, string userEmail)
        {
            var (_, userGuid) = await _db.GetUserIdByEmailAsync(userEmail);
            if (userGuid is null) return ApiResponse.Fail("User not found");

            var booking = await _db.GetBookingByGuidAsync(request.BookingId);
            if (booking is null) return ApiResponse.Fail("Booking not found");

            var voucherNumber = $"1BILL{request.BookingId[..Math.Min(4, request.BookingId.Length)]}{Random.Shared.Next(10000, 99999):D5}";
            var expiryDate    = DateTime.UtcNow.AddDays(3).ToString("o");
            await _db.CreatePaymentAsync(userGuid, request.BookingId, request.Amount, "Voucher", voucherNumber);
            return ApiResponse.Ok(new { voucherNumber, expiryDate, amount = request.Amount }, "Voucher generated.");
        }

        public async Task<ApiResponse> InitiatePayFastAsync(PayFastInitiateRequest request, string userEmail)
        {
            var (_, userGuid) = await _db.GetUserIdByEmailAsync(userEmail);
            if (userGuid is null) return ApiResponse.Fail("User not found");

            var booking = await _db.GetBookingByGuidAsync(request.BookingId);
            if (booking is null) return ApiResponse.Fail("Booking not found");

            var amount  = booking.TryGetValue("totalAmount", out var a) ? Convert.ToDouble(a) : 0;
            var orderId = $"WN-{request.BookingId}-{Random.Shared.Next(100000, 999999)}";

            var payload = _payFast.BuildPayload(request.BookingId, amount,
                $"WorkNest Booking #{request.BookingId}",
                request.CustomerEmail, request.CustomerName, orderId);

            await _db.CreatePaymentAsync(userGuid, request.BookingId, amount, "PayFast", orderId);
            return ApiResponse.Ok(payload, "PayFast payment initiated.");
        }

        public async Task<ApiResponse> HandlePayFastNotifyAsync(Dictionary<string, string> formData)
        {
            if (!_payFast.VerifySignature(new Dictionary<string, string>(formData)))
                return ApiResponse.Fail("Invalid PayFast signature");

            var paymentStatus = formData.TryGetValue("payment_status", out var ps)
                ? ps.ToUpperInvariant() : "";
            var orderId = formData.TryGetValue("order_id", out var oid) ? oid : "";

            if (!string.IsNullOrWhiteSpace(orderId))
                await _db.UpdatePaymentStatusByRefAsync(orderId,
                    paymentStatus == "COMPLETE" ? "Paid" : "Failed");

            return ApiResponse.Ok();
        }
    }
}
