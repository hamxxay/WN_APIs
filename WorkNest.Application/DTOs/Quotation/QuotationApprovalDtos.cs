using System;
using System.Collections.Generic;

namespace WorkNest.Application.DTOs.Quotation
{
    /// <summary>The approval columns of one WN_Quotations row (script QAPR_01) plus the names the screens show.</summary>
    public sealed class QuotationApprovalInfo
    {
        public int QuotationId { get; set; }
        public string? DiscountReason { get; set; }
        public string? ApprovalStatus { get; set; }
        public int? ManagerUserId { get; set; }
        public string? ManagerName { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? DecidedAt { get; set; }
        public int? DecidedByUserId { get; set; }
        public string? Note { get; set; }
        public DateTime? LastReminderAt { get; set; }
        public int? CreatedByUserId { get; set; }
        /// <summary>Who last submitted / resubmitted it (from the history); the one told of the decision.</summary>
        public int? SubmittedByUserId { get; set; }
        public string? ExecutiveName { get; set; }
        public int? LocationId { get; set; }
    }

    /// <summary>One line of the quotation's history (WN_QuotationHistory): GET api/quotation/{id}/history.</summary>
    public sealed class QuotationHistoryEntryDto
    {
        public DateTime At { get; set; }
        public string? ByName { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? FromDiscount { get; set; }
        public string? ToDiscount { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>A history line to write (the repository stamps At in UTC when it is null).</summary>
    public sealed class QuotationHistoryWrite
    {
        public int QuotationId { get; set; }
        public int? ByUserId { get; set; }
        public string? ByName { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? FromDiscount { get; set; }
        public string? ToDiscount { get; set; }
        public string? Note { get; set; }
        public DateTime? At { get; set; }
    }

    /// <summary>A quotation waiting for (or returned by) the sales manager: GET api/quotation-approvals.</summary>
    public class QuotationApprovalRowDto
    {
        public int QuotationId { get; set; }
        public string? QuotationNumber { get; set; }
        public string? CustomerName { get; set; }
        public string? Company { get; set; }
        public string? SpaceName { get; set; }
        public string? LocationName { get; set; }
        public string? ExecutiveName { get; set; }
        public decimal BasePrice { get; set; }
        public string DiscountType { get; set; } = "Percentage";
        public decimal DiscountValue { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal FinalAmount { get; set; }
        public decimal? CapPercent { get; set; }
        public string? DiscountReason { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public string? ApprovalStatus { get; set; }
    }

    public sealed class QuotationApprovalDetailDto : QuotationApprovalRowDto
    {
        public List<QuotationHistoryEntryDto> History { get; set; } = new();
    }

    public sealed class ApproveQuotationRequest
    {
        /// <summary>"Percentage" | "Amount"; with <see cref="DiscountValue"/> the manager edits the discount before approving.</summary>
        public string? DiscountType { get; set; }
        public decimal? DiscountValue { get; set; }
        public string? Note { get; set; }
    }

    public sealed class SendBackQuotationRequest
    {
        public string? Note { get; set; }
    }

    /// <summary>The send was held for the sales manager's approval instead of going to the customer.</summary>
    public sealed class QuotationApprovalHold
    {
        public string Message { get; set; } = string.Empty;
        public string ApprovalStatus { get; set; } = QuotationApprovalHoldStatus.PendingManager;
    }

    public static class QuotationApprovalHoldStatus
    {
        public const string PendingManager = "PendingManager";
    }

    /// <summary>Who is acting on an approval (resolved from the signed-in user by the controller).</summary>
    public sealed record QuotationApprovalCaller(int UserId, bool IsSuperAdmin, IReadOnlyCollection<int>? LocationIds);
}
