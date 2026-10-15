using WorkNest.Application.DTOs.Complaint;
using WorkNest.Application.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IComplaintService
    {
        /// <summary>Complaints page; <paramref name="branch"/> = the branch filter (null = every complaint).</summary>
        Task<(List<ComplaintDto> Items, int Total)> GetListAsync(int page, int limit, string? search, string? status,
            BranchFilter? branch = null);
        /// <summary>
        /// The branch of a new complaint (<paramref name="locationId"/>, else the location named <paramref name="branchName"/>)
        /// or of an existing one being changed (<paramref name="change"/>), by the branch rules of <paramref name="scope"/>.
        /// </summary>
        Task<BranchChoice> ChooseBranchAsync(int? locationId, string? branchName, BranchScope scope, bool change = false);
        /// <summary>Complaint entered by staff; <paramref name="branch"/> = checked by <see cref="ChooseBranchAsync"/> (its name is saved as Branch).</summary>
        Task<ApiResponse> CreateManualAsync(ComplaintCreateRequest request, int? actorId, BranchChoice? branch = null, BranchScope? scope = null);
        /// <summary>Changes the status; on Resolved the customer is asked on WhatsApp whether the issue is fixed.
        /// <paramref name="scope"/>: only complaints it can see.</summary>
        Task<ApiResponse> UpdateStatusAsync(int id, ComplaintStatusUpdateRequest request, int? actorId, BranchScope? scope = null);
        /// <summary>Sets / changes a complaint's branch (checked by <see cref="ChooseBranchAsync"/>); only complaints the scope can see.</summary>
        Task<ApiResponse> UpdateBranchAsync(int id, BranchOption? location, BranchScope scope, int? actorId);

        // WhatsApp chatbot
        Task<ApiResponse> CreateFromChatbotAsync(ChatbotComplaintRequest request);
        Task<ApiResponse> HandleResolutionReplyAsync(ChatbotResolutionReplyRequest request);
    }
}
