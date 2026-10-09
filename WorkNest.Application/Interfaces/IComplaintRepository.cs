using WorkNest.Application.DTOs.Complaint;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Data access for dbo.WN_Complaints (created by WN_Complaints.txt).</summary>
    public interface IComplaintRepository
    {
        Task<(List<ComplaintDto> Items, int Total)> GetListAsync(int page, int limit, string? search, string? status);
        Task<ComplaintDto?> GetByIdAsync(int id);
        /// <summary>Inserts a complaint; returns its Id, or null when the ComplaintNo is already taken.</summary>
        Task<int?> InsertAsync(ComplaintDto complaint, int? createdById);
        Task UpdateStatusAsync(int id, string status, string? staffNotes, int? updatedById);
        Task MarkResolvedNotificationSentAsync(int id);
        /// <summary>Newest complaint for this phone that was notified as resolved and has no customer reply yet.</summary>
        Task<ComplaintDto?> GetAwaitingResolutionReplyAsync(IEnumerable<string> phoneVariants, DateTime notifiedSince);
        Task SetResolutionResponseAsync(int id, string response, string? newStatus);
        Task<List<int>> GetOpenIdsAsync();
    }
}
