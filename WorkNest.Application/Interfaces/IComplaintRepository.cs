using WorkNest.Application.DTOs.Complaint;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Data access for dbo.WN_Complaints (created by WN_Complaints.txt).</summary>
    public interface IComplaintRepository
    {
        /// <summary>
        /// Complaints page. <paramref name="branch"/>: complaints whose Branch is one of these locations' names, plus (when
        /// IncludeUnassigned) those without a branch or with a branch that is no location; null / none = every complaint.
        /// </summary>
        Task<(List<ComplaintDto> Items, int Total)> GetListAsync(int page, int limit, string? search, string? status,
            WorkNest.Application.Helpers.BranchFilter? branch = null);
        Task<ComplaintDto?> GetByIdAsync(int id);
        /// <summary>Inserts a complaint; returns its Id, or null when the ComplaintNo is already taken.</summary>
        Task<int?> InsertAsync(ComplaintDto complaint, int? createdById);
        Task UpdateStatusAsync(int id, string status, string? staffNotes, int? updatedById);
        Task MarkResolvedNotificationSentAsync(int id);
        /// <summary>Newest complaint for this phone that was notified as resolved and has no customer reply yet.</summary>
        Task<ComplaintDto?> GetAwaitingResolutionReplyAsync(IEnumerable<string> phoneVariants, DateTime notifiedSince);
        Task SetResolutionResponseAsync(int id, string response, string? newStatus);
        /// <summary>Open complaints; <paramref name="locationIds"/>: of these locations + those without a branch (null = all).</summary>
        Task<List<int>> GetOpenIdsAsync(IReadOnlyCollection<int>? locationIds = null);
        /// <summary>Sets the Branch (a location name; null = none). Rows changed, or null when the database update BR_01 has not been run.</summary>
        Task<int?> UpdateBranchAsync(int id, string? branch, int? updatedById);
    }
}
