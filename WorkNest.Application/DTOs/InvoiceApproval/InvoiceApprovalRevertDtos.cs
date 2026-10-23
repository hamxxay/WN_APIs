namespace WorkNest.Application.DTOs.InvoiceApproval
{
    /// <summary>Body of POST api/invoice/{id}/revert-approval (super admin only).</summary>
    public class RevertInvoiceApprovalRequest
    {
        /// <summary>Why the invoice goes back to Pending Approval: required, 1 to 500 characters after trimming.</summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// What the API reads before calling dbo.WN_Invoices_RevertApproval (INV_REVERT_01), so the rules are checked before
    /// the procedure ("validate before SP"); the procedure checks the same rules again with the invoice row locked.
    /// </summary>
    public class InvoiceRevertState
    {
        public int InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        /// <summary>'Approved', 'PendingApproval' or null (null = approved).</summary>
        public string? ApprovalStatus { get; set; }
        /// <summary>StatusId 40 / OrderStatus 'Delete' / 'Deleted'.</summary>
        public bool IsDeleted { get; set; }
        /// <summary>StatusId 5 / OrderStatus 'Cancelled'.</summary>
        public bool IsCancelled { get; set; }
        /// <summary>OneBillStatus 1 (claimed) or 2 (created), or a OneBillNumber / EzPayInvoiceId.</summary>
        public bool OnOneBill { get; set; }
        /// <summary>PaidTotal above 0, status Paid / Partial, an active fee receipt, a paid WN_Payments row or an applied 1Bill payment.</summary>
        public bool HasMoney { get; set; }
    }

    /// <summary>The one row dbo.WN_Invoices_RevertApproval returns.</summary>
    public class InvoiceApprovalRevertResult
    {
        public int InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? OldApprovalStatus { get; set; }
        public string NewApprovalStatus { get; set; } = InvoiceApprovalRevertRules.PendingApproval;
        public int CancelledDeliveryRows { get; set; }
    }

    /// <summary>
    /// A rule raised by dbo.WN_Invoices_RevertApproval (RAISERROR, error 50000). State 1 = rule broken (400), 2 = not found (404).
    /// </summary>
    public sealed class InvoiceApprovalRevertException : Exception
    {
        public byte State { get; }

        public InvoiceApprovalRevertException(string message, byte state, Exception? inner = null) : base(message, inner)
        {
            State = state;
        }

        public bool IsNotFound => State == 2;
    }

    /// <summary>The database update INV_REVERT_01 (or the invoice approval columns it needs) is not on this database.</summary>
    public sealed class InvoiceApprovalRevertDatabaseMissingException : Exception
    {
        public InvoiceApprovalRevertDatabaseMissingException(Exception? inner = null)
            : base(InvoiceApprovalRevertRules.DatabaseMissingMessage, inner) { }
    }

    /// <summary>
    /// Rules of moving an invoice from Approved back to Pending Approval. The messages are the procedure's own
    /// (dbo.WN_Invoices_RevertApproval, Database/Invoicing/INV_REVERT_01_revert_approval.txt), checked in the same order.
    /// </summary>
    public static class InvoiceApprovalRevertRules
    {
        public const string PendingApproval = "PendingApproval";
        public const int MaxReasonLength = 500;

        public const string ReasonRequiredMessage = "Enter the reason for moving this invoice back to Pending Approval.";
        public static readonly string ReasonTooLongMessage = $"The reason can be at most {MaxReasonLength} characters.";
        public const string NotFoundMessage = "Invoice not found.";
        public const string DeletedMessage = "This invoice is deleted and can't be reverted.";
        public const string CancelledMessage = "This invoice is cancelled and can't be reverted.";
        public const string AlreadyPendingMessage = "This invoice is already Pending Approval.";
        public const string OneBillMessage = "This invoice is already on 1Bill/EzPay and can't be reverted.";
        public const string MoneyMessage = "Payment has been received against this invoice. Void its fee receipt(s) first, then move it back to Pending Approval.";
        public const string SuccessMessage = "Invoice moved back to Pending Approval. The customer already received the original invoice email.";
        public const string ForbiddenMessage = "Only a super admin can move an invoice back to Pending Approval.";
        public const string DatabaseMissingMessage =
            "Moving invoices back to Pending Approval needs the database update INV_REVERT_01. Ask the WorkNest team to run it.";

        /// <summary>The trimmed reason, or the message why it is not valid.</summary>
        public static (string? Reason, string? Error) ValidateReason(string? reason)
        {
            var trimmed = reason?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return (null, ReasonRequiredMessage);
            if (trimmed.Length > MaxReasonLength) return (null, ReasonTooLongMessage);
            return (trimmed, null);
        }

        /// <summary>Null when the invoice may be reverted, else the message (all are HTTP 400; a missing invoice is 404).</summary>
        public static string? Check(InvoiceRevertState state)
        {
            if (state.IsDeleted) return DeletedMessage;
            if (state.IsCancelled) return CancelledMessage;
            if (string.Equals(state.ApprovalStatus, PendingApproval, StringComparison.Ordinal)) return AlreadyPendingMessage;
            if (state.ApprovalStatus != null && !string.Equals(state.ApprovalStatus, "Approved", StringComparison.Ordinal))
                return $"Only approved invoices can be moved back to Pending Approval (this one is '{state.ApprovalStatus}').";
            if (state.OnOneBill) return OneBillMessage;
            if (state.HasMoney) return MoneyMessage;
            return null;
        }
    }
}
