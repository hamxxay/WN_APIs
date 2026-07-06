using WorkNest.Application.DTOs.Membership;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class MembershipService : IMembershipService
    {
        private readonly IDbRepository _db;
        public MembershipService(IDbRepository db) => _db = db;

        public async Task<IEnumerable<object>> GetAllMembershipsAsync() =>
            (await _db.GetAllMembershipsAsync()).Cast<object>();

        public async Task<ApiResponse> GetMembershipSummaryAsync(int id)
        {
            var memberships = await _db.GetAllMembershipsAsync();
            var membership  = memberships.FirstOrDefault(m =>
                m.TryGetValue("id", out var mid) && Convert.ToInt32(mid) == id);
            if (membership is null) return ApiResponse.Fail("Membership not found");

            var payments   = (await _db.GetPaymentsByMembershipIdAsync(id)).ToList();
            var paidAmount = payments
                .Where(p => p.TryGetValue("paymentStatus", out var s) && s?.ToString() == "Paid")
                .Sum(p => p.TryGetValue("amount", out var a) ? Convert.ToDouble(a) : 0);

            int? daysRemaining = null;
            if (membership.TryGetValue("endDate", out var ed) && ed is not null)
            {
                try
                {
                    var delta = (DateTime.Parse(ed.ToString()!) - DateTime.UtcNow).Days;
                    daysRemaining = Math.Max(0, delta);
                }
                catch { }
            }

            return ApiResponse.Ok(new
            {
                membership,
                stats = new
                {
                    totalPayments    = payments.Count,
                    paidPayments     = payments.Count(p => p.TryGetValue("paymentStatus", out var s) && s?.ToString() == "Paid"),
                    pendingPayments  = payments.Count(p => p.TryGetValue("paymentStatus", out var s) && s?.ToString() == "Pending"),
                    failedPayments   = payments.Count(p => p.TryGetValue("paymentStatus", out var s) && s?.ToString() == "Failed"),
                    refundedPayments = payments.Count(p => p.TryGetValue("paymentStatus", out var s) && s?.ToString() == "Refunded"),
                    paidAmount,
                    isExpired        = daysRemaining == 0,
                    daysRemaining,
                },
                recentPayments         = payments.Take(5),
                userMembershipHistory  = Array.Empty<object>(),
            });
        }

        public async Task<ApiResponse> CreateMembershipAsync(MembershipCreateRequest request)
        {
            var id = await _db.CreateMembershipAsync(request.UserId, request.PlanId, request.StartDate);
            return ApiResponse.Ok(new { id }, "Membership created.");
        }

        public async Task<ApiResponse> UpdateMembershipStatusAsync(int id, string status)
        {
            await _db.UpdateMembershipStatusAsync(id, status);
            return ApiResponse.Ok("Membership status updated.");
        }

        public async Task<ApiResponse> DeleteMembershipAsync(int id)
        {
            await _db.SoftDeleteMembershipAsync(id);
            return ApiResponse.Ok("Membership deleted.");
        }
    }
}
