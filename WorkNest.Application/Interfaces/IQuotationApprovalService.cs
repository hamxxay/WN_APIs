using WorkNest.Application.DTOs.Quotation;

namespace WorkNest.Application.Interfaces
{
    public interface IQuotationApprovalService
    {
        /// <summary>
        /// Called before ANY send path (send, send-email, version send). Null = go on and send as before (not a sales executive, no manual
        /// discount, already approved, or approval not set up). Otherwise the quotation is now PendingManager and was NOT sent: the hold
        /// says what to tell the executive. Throws <see cref="InvalidOperationException"/> (400) for an executive without a sales manager.
        /// </summary>
        Task<QuotationApprovalHold?> HoldForApprovalAsync(int quotationId, int? actorUserId, bool actorIsSalesExecutive, CancellationToken ct = default);

        /// <summary>History line "sent_to_customer" after a send went through (never fails the send).</summary>
        Task RecordSentAsync(int quotationId, int? actorUserId, CancellationToken ct = default);

        /// <summary>History line "edited" on a new version made from <paramref name="sourceQuotationId"/> and the reason carried over (never fails).</summary>
        Task RecordNewVersionAsync(int sourceQuotationId, int newQuotationId, int? actorUserId, CancellationToken ct = default);

        Task<IReadOnlyList<QuotationApprovalRowDto>> ListAsync(QuotationApprovalCaller caller, bool pendingOnly, CancellationToken ct = default);
        Task<QuotationApprovalDetailDto> GetAsync(QuotationApprovalCaller caller, int quotationId, CancellationToken ct = default);
        /// <summary>Approve (optionally editing the discount first), then send to the customer. Returns the message to show.</summary>
        Task<(string Message, QuotationApprovalDetailDto Row)> ApproveAsync(QuotationApprovalCaller caller, int quotationId, ApproveQuotationRequest request, CancellationToken ct = default);
        Task<QuotationApprovalDetailDto> SendBackAsync(QuotationApprovalCaller caller, int quotationId, SendBackQuotationRequest request, CancellationToken ct = default);

        /// <summary>History of a quotation for a staff caller who may see it; null = the caller may not (404).</summary>
        Task<IReadOnlyList<QuotationHistoryEntryDto>?> GetHistoryAsync(int quotationId, int? callerUserId, bool callerSeesAll, CancellationToken ct = default);

        /// <summary>The 24 h reminders: one per quotation per day while it waits. Returns how many were sent.</summary>
        Task<int> SendDueRemindersAsync(CancellationToken ct = default);
    }
}
