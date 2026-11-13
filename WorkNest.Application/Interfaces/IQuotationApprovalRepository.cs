using WorkNest.Application.DTOs.Offers;
using WorkNest.Application.DTOs.Quotation;

namespace WorkNest.Application.Interfaces
{
    /// <summary>The discount of a quotation as the sales manager approved it (amounts already worked out by the quotation calculation).</summary>
    public sealed record QuotationDiscountUpdate(int QuotationId, string DiscountType, decimal DiscountPercentage, decimal DiscountAmount,
        decimal TotalAmount, decimal SecurityDeposit, int? ByUserId, OfferSnapshot? Snapshot);

    /// <summary>
    /// Quotation discount approval (script QAPR_01_quotation_approval: seven columns on dbo.WN_Quotations and dbo.WN_QuotationHistory),
    /// plain SQL. Before the script has run <see cref="IsSetUpAsync"/> is false and every read answers "nothing": quotations keep their
    /// old behaviour (no reason stored, no approval). Writes are only called after <see cref="IsSetUpAsync"/> is true.
    /// </summary>
    public interface IQuotationApprovalRepository
    {
        /// <summary>True once the script has run (columns and history table exist). A yes is remembered; a no is re-checked every minute.</summary>
        Task<bool> IsSetUpAsync(CancellationToken ct = default);

        Task<QuotationApprovalInfo?> GetInfoAsync(int quotationId, CancellationToken ct = default);

        Task SetDiscountReasonAsync(int quotationId, string? reason, CancellationToken ct = default);

        /// <summary>PendingManager for this manager (not when it already is). False = it was already waiting.</summary>
        Task<bool> SubmitAsync(int quotationId, int managerUserId, DateTime nowUtc, CancellationToken ct = default);

        /// <summary>Approved / SentBack, only while PendingManager. False = it was not waiting (someone else decided first).</summary>
        Task<bool> DecideAsync(int quotationId, string status, int decidedByUserId, string? note, DateTime nowUtc, CancellationToken ct = default);

        Task AddHistoryAsync(QuotationHistoryWrite entry, CancellationToken ct = default);

        /// <summary>Oldest first.</summary>
        Task<IReadOnlyList<QuotationHistoryEntryDto>> GetHistoryAsync(int quotationId, CancellationToken ct = default);

        /// <summary>Quotations routed to <paramref name="managerUserId"/> (null = every manager). pendingOnly: PendingManager only; otherwise every quotation that went through approval.
        /// locationIds: only quotations of these locations (null = all). Newest submission first.</summary>
        Task<IReadOnlyList<QuotationApprovalInfo>> ListAsync(int? managerUserId, bool pendingOnly, IReadOnlyCollection<int>? locationIds, CancellationToken ct = default);

        /// <summary>PendingManager quotations whose last reminder (or submission) is at or before <paramref name="cutoffUtc"/>.</summary>
        Task<IReadOnlyList<QuotationApprovalInfo>> GetDueRemindersAsync(DateTime cutoffUtc, CancellationToken ct = default);

        Task MarkRemindedAsync(int quotationId, DateTime nowUtc, CancellationToken ct = default);

        /// <summary>A new version keeps the reason of the quotation it was made from (approval status starts empty).</summary>
        Task CopyReasonToNewVersionAsync(int sourceQuotationId, int newQuotationId, CancellationToken ct = default);

        Task UpdateDiscountAsync(QuotationDiscountUpdate update, CancellationToken ct = default);
    }

    /// <summary>The script QAPR_01 has not run: the controller answers 503 with <see cref="Helpers.QuotationApprovalRules.NotSetUpMessage"/>.</summary>
    public sealed class QuotationApprovalNotSetUpException : Exception
    {
        public QuotationApprovalNotSetUpException() : base(Helpers.QuotationApprovalRules.NotSetUpMessage) { }
    }

    /// <summary>No such quotation (404).</summary>
    public sealed class QuotationApprovalNotFoundException : Exception
    {
        public QuotationApprovalNotFoundException() : base("Quotation not found.") { }
    }

    /// <summary>A sales manager acting on a quotation that was not routed to them / outside their locations (403).</summary>
    public sealed class QuotationApprovalForbiddenException : Exception
    {
        public QuotationApprovalForbiddenException(string message) : base(message) { }
    }

    /// <summary>A rule of the approval was broken (400): the message is plain English for the CMS and the app.</summary>
    public sealed class QuotationApprovalRuleException : Exception
    {
        public QuotationApprovalRuleException(string message) : base(message) { }
    }
}
