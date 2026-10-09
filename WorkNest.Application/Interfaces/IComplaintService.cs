using WorkNest.Application.DTOs.Complaint;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IComplaintService
    {
        Task<(List<ComplaintDto> Items, int Total)> GetListAsync(int page, int limit, string? search, string? status);
        Task<ApiResponse> CreateManualAsync(ComplaintCreateRequest request, int? actorId);
        /// <summary>Changes the status; on Resolved the customer is asked on WhatsApp whether the issue is fixed.</summary>
        Task<ApiResponse> UpdateStatusAsync(int id, ComplaintStatusUpdateRequest request, int? actorId);

        // WhatsApp chatbot
        Task<ApiResponse> CreateFromChatbotAsync(ChatbotComplaintRequest request);
        Task<ApiResponse> HandleResolutionReplyAsync(ChatbotResolutionReplyRequest request);
    }
}
