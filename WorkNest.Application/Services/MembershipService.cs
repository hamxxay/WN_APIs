using WorkNest.Application.DTOs.Membership;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class MembershipService : IMembershipService
    {
        private readonly IDbRepository _db;
        public MembershipService(IDbRepository db) => _db = db;

        public async Task<(IEnumerable<object> Items, int Total)> GetMembershipsAsync(int page, int limit, string? search)
        {
            var (rows, total) = await _db.GetMembershipsAsync(page, limit, search);
            return (rows.Cast<object>(), total);
        }

        public async Task<ApiResponse> GetMembershipSummaryAsync(int id)
        {
            var data = await _db.GetMembershipSummaryAsync(id);
            if (data is null) return ApiResponse.Fail("Membership not found");
            return ApiResponse.Ok(data);
        }

        public async Task<ApiResponse> CreateMembershipAsync(MembershipCreateRequest request, int? actorId)
        {
            var result = await _db.InsertMembershipAsync(
                request.UserId, request.PlanId,
                request.StartOn, request.EndOn,
                request.AutoRenew, request.Notes, actorId);
            return ApiResponse.Ok(result, "Membership created.");
        }

        public async Task<ApiResponse> UpdateMembershipStatusAsync(int id, byte statusId, int? actorId)
        {
            await _db.UpdateMembershipStatusAsync(id, statusId, actorId);
            return ApiResponse.Ok("Membership status updated.");
        }

        public async Task<ApiResponse> DeleteMembershipAsync(int id)
        {
            await _db.DeleteMembershipAsync(id);
            return ApiResponse.Ok("Membership deleted.");
        }
    }
}
