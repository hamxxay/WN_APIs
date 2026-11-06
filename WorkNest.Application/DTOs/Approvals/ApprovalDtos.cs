namespace WorkNest.Application.DTOs.Approvals
{
    /// <summary>Approver e-mail (null until the super admin sets it) and the link expiry in days.</summary>
    public sealed record ApprovalSettings(string? ApproverEmail, int LinkExpiryDays, DateTime UpdatedOn, string? UpdatedBy);

    public sealed record ApprovalCategory(int Id, string Name, bool IsActive);

    /// <summary>One person for a location + category (dbo.WN_ApprovalAssignees).</summary>
    public sealed record ApprovalAssignee(int Id, int LocationId, string? LocationName, int CategoryId, string CategoryName,
        int UserId, string? UserName, string? Email, bool? UserIsActive);

    /// <summary>A person a request was sent to when it was approved.</summary>
    public sealed record ApprovalRequestAssignee(int UserId, string? Name, string? Email);

    /// <summary>One row of the request list. TotalCount is the count before paging.</summary>
    public sealed record ApprovalRequestRow(int Id, int LocationId, string? LocationName, int CategoryId, string CategoryName, string Title,
        string Status, int RequestedByUserId, string? RequestedByName, DateTime RequestedAt, DateTime? DecidedAt, DateTime? DoneAt, int TotalCount);

    public sealed record ApprovalRequestPage(IReadOnlyList<ApprovalRequestRow> Items, int TotalCount);

    /// <summary>One request with its names and the people it was sent to (UTC times).</summary>
    public sealed record ApprovalRequestDetail(int Id, int LocationId, string? LocationName, int CategoryId, string CategoryName,
        string Title, string Details, string? Reason, string Status, int RequestedByUserId, string? RequestedByName, string? RequestedByEmail,
        DateTime RequestedAt, DateTime TokenExpiresAt, DateTime? DecidedAt, string? DecisionNote, int? DoneByUserId, string? DoneByName,
        DateTime? DoneAt, string? DoneNote, IReadOnlyList<ApprovalRequestAssignee> Assignees);

    /// <summary>What the public approval page shows for a link (no internal ids beyond the request).</summary>
    public sealed record ApprovalPublicRequest(int Id, string? LocationName, string CategoryName, string Title, string Details, string? Reason,
        string Status, string? RequestedByName, DateTime RequestedAt, DateTime TokenExpiresAt, DateTime? DecidedAt, string? DecisionNote);

    /// <summary>The result of a decision: the request and its new status.</summary>
    public sealed record ApprovalDecided(int Id, string Status);

    /// <summary>A new request as the repository stores it. TokenHash is the SHA-256 of the e-mailed link token.</summary>
    public sealed record ApprovalNewRequest(int LocationId, int CategoryId, string Title, string Details, string? Reason,
        int RequestedByUserId, string ApproverEmail, byte[] TokenHash, DateTime TokenExpiresAtUtc);
}
