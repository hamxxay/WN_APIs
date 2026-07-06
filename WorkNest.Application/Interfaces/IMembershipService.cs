using WorkNest.Application.DTOs.Membership;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IMembershipService
    {
        Task<IEnumerable<object>> GetAllMembershipsAsync();
        Task<ApiResponse> GetMembershipSummaryAsync(int id);
        Task<ApiResponse> CreateMembershipAsync(MembershipCreateRequest request);
        Task<ApiResponse> UpdateMembershipStatusAsync(int id, string status);
        Task<ApiResponse> DeleteMembershipAsync(int id);
    }
}
