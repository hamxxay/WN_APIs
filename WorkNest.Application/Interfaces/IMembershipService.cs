using WorkNest.Application.DTOs.Membership;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IMembershipService
    {
        Task<(IEnumerable<object> Items, int Total)> GetMembershipsAsync(int page, int limit, string? search);
        Task<ApiResponse> GetMembershipSummaryAsync(int id);
        Task<ApiResponse> CreateMembershipAsync(MembershipCreateRequest request, int? actorId);
        Task<ApiResponse> UpdateMembershipStatusAsync(int id, byte statusId, int? actorId);
        Task<ApiResponse> DeleteMembershipAsync(int id);
    }
}
