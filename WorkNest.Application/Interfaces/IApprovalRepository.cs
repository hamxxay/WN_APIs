using WorkNest.Application.DTOs.Approvals;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Approvals tables and procedures of script APPROVAL_01_approvals (dbo.WN_Approval_*). SQL error 208 / 2812
    /// (table / procedure missing) = the script has not run: <see cref="ApprovalsMissingException"/>.
    /// </summary>
    public interface IApprovalRepository
    {
        Task<ApprovalSettings?> GetSettingsAsync(CancellationToken ct = default);
        Task SaveSettingsAsync(string? approverEmail, string? updatedBy, CancellationToken ct = default);

        Task<IReadOnlyList<ApprovalCategory>> GetCategoriesAsync(bool activeOnly, CancellationToken ct = default);
        /// <summary>Adds when <paramref name="id"/> is null, else renames / turns on or off. Returns the id. A duplicate name throws <see cref="ApprovalCategoryException"/>.</summary>
        Task<int> SaveCategoryAsync(int? id, string name, bool isActive, string? by, CancellationToken ct = default);

        Task<IReadOnlyList<ApprovalAssignee>> GetAssigneesAsync(int? locationId, CancellationToken ct = default);
        /// <summary>Adds the person; a repeat is ignored.</summary>
        Task AddAssigneeAsync(int locationId, int categoryId, int userId, string? by, CancellationToken ct = default);
        Task DeleteAssigneeAsync(int id, CancellationToken ct = default);

        /// <summary>Stores a Pending request and returns its id.</summary>
        Task<int> CreateRequestAsync(ApprovalNewRequest request, CancellationToken ct = default);
        /// <summary>Null when the request does not exist.</summary>
        Task<ApprovalRequestDetail?> GetRequestAsync(int id, CancellationToken ct = default);
        /// <summary><paramref name="locationIds"/> null = all locations; <paramref name="assigneeUserId"/> also lists the requests sent to that person.</summary>
        Task<ApprovalRequestPage> GetRequestsAsync(IReadOnlyCollection<int>? locationIds, int? assigneeUserId, string? status, int? categoryId,
            int page, int pageSize, CancellationToken ct = default);
        /// <summary>Changes nothing. Null for an unknown link.</summary>
        Task<ApprovalPublicRequest?> GetRequestByTokenAsync(byte[] tokenHash, CancellationToken ct = default);
        /// <summary>Decision Approved or Rejected. Null when the link is unknown, used or expired.</summary>
        Task<ApprovalDecided?> DecideAsync(byte[] tokenHash, string decision, string? note, CancellationToken ct = default);
        /// <summary>False when the request is not Approved.</summary>
        Task<bool> MarkDoneAsync(int id, int doneByUserId, string? note, CancellationToken ct = default);
        /// <summary>Makes an Expired request Pending with a new link. False when it is not Expired.</summary>
        Task<bool> ResendRequestAsync(int id, string approverEmail, byte[] tokenHash, DateTime tokenExpiresAtUtc, CancellationToken ct = default);
        /// <summary>Expires the Pending requests past their link expiry and returns their ids.</summary>
        Task<IReadOnlyList<int>> ExpireDueAsync(CancellationToken ct = default);

        /// <summary>Active staff for the people picker: customers, CEO and Sales Manager accounts left out.</summary>
        Task<IReadOnlyList<ApprovalStaff>> GetStaffAsync(CancellationToken ct = default);
        /// <summary>Id and name of every location.</summary>
        Task<IReadOnlyList<ApprovalLocation>> GetLocationsAsync(CancellationToken ct = default);
    }

    /// <summary>The database update APPROVAL_01_approvals has not run (SQL errors 208 / 2812).</summary>
    public sealed class ApprovalsMissingException : Exception
    {
        /// <summary>What the CMS shows (503). No "sql" in it: the CMS hides messages containing that word.</summary>
        public const string UserMessage = "Database update needed: run the script APPROVAL_01_approvals.";
        public ApprovalsMissingException(Exception? inner = null) : base(UserMessage, inner) { }
    }

    /// <summary>A category name is already used.</summary>
    public sealed class ApprovalCategoryException : Exception
    {
        public ApprovalCategoryException(string message) : base(message) { }
    }
}
