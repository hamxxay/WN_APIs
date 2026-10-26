using WorkNest.Application.DTOs.Membership;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IMembershipService
    {
        Task<(IEnumerable<object> Items, int Total)> GetMembershipsAsync(int page, int limit, string? search);
        /// <summary>
        /// The list limited to the members of these locations (location-bound staff); null = every membership. Throws
        /// <see cref="WorkNest.Application.Helpers.LocationScopeUpdateMissingException"/> for a scoped call before SEC_LOC_02.
        /// </summary>
        Task<(IEnumerable<object> Items, int Total)> GetMembershipsAsync(int page, int limit, string? search, IReadOnlyCollection<int>? locationIds);
        Task<ApiResponse> GetMembershipSummaryAsync(int id);
        Task<ApiResponse> CreateMembershipAsync(MembershipCreateRequest request, int? actorId);
        Task<ApiResponse> UpdateMembershipStatusAsync(int id, byte statusId, int? actorId);
        Task<ApiResponse> DeleteMembershipAsync(int id);
    }
}
