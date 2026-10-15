using WorkNest.Application.DTOs.Contact;
using WorkNest.Application.Helpers;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IContactService
    {
        /// <summary>Tour inquiries; <paramref name="branch"/> = the branch filter (null = every inquiry). Rows have locationId / locationName.</summary>
        Task<(IEnumerable<object> Items, int Total)> GetContactsAsync(int page, int limit, string? search, string? inquiryType = null,
            BranchFilter? branch = null);
        Task<IEnumerable<object>> GetRecentContactsAsync(int top, BranchFilter? branch = null);
        /// <summary>
        /// The branch of a new inquiry (<paramref name="change"/> false) or of an existing one being changed, by the
        /// branch rules of <paramref name="scope"/> (see <see cref="BranchScope.ForNew"/> / <see cref="BranchScope.ForChange"/>).
        /// </summary>
        Task<BranchChoice> ChooseBranchAsync(int? requested, BranchScope scope, bool change = false);
        /// <summary>fromStaff: added by staff on the Tour Inquiries page (Type WalkIn or Reference); otherwise a website / app form (Type Website).
        /// <paramref name="locationId"/>: the branch already checked by <see cref="ChooseBranchAsync"/>.</summary>
        Task<ApiResponse> CreateContactAsync(ContactRequest request, string contactType, string? userEmail, bool fromStaff = false,
            int? locationId = null);
        /// <summary>Sets / changes an inquiry's branch (checked by <see cref="ChooseBranchAsync"/>); only inquiries the scope can see.</summary>
        Task<ApiResponse> UpdateLocationAsync(Guid publicId, BranchOption? location, BranchScope scope, string? actorEmail);
        /// <summary>Changes an inquiry's Type (Website, WhatsApp, WalkIn or Reference) and, for Reference, who referred the customer.</summary>
        Task<ApiResponse> UpdateInquiryTypeAsync(Guid publicId, ContactInquiryTypeRequest request, string? actorEmail);
        Task<ApiResponse> UpdateContactStatusAsync(int id, byte statusId, int? actorId);
        Task<ApiResponse> DeleteContactAsync(int id);
        /// <summary>Records staff feedback on an inquiry and updates its status (not interested / converted close it).</summary>
        /// <summary>Book a Tour finished on the WhatsApp chatbot: recorded as a Tour Inquiry (no email).</summary>
        Task<ApiResponse> CreateChatbotTourInquiryAsync(WorkNest.Application.DTOs.Complaint.ChatbotTourInquiryRequest request);
        Task<ApiResponse> SaveFeedbackAsync(int id, ContactFeedbackRequest request, int? actorId);
    }
}
