namespace WorkNest.Application.DTOs.Account
{
    /// <summary>Body of POST api/account/delete: { "confirm": "DELETE" }.</summary>
    public sealed class AccountDeleteRequest
    {
        public string? Confirm { get; set; }
    }

    /// <summary>
    /// Whether the signed-in user may delete their account (GET api/account/delete-check, and the data of a refused
    /// POST api/account/delete). <see cref="Reason"/> is a plain sentence ready to show; null when it can be deleted.
    /// </summary>
    public sealed class AccountDeletionCheck
    {
        public bool CanDelete { get; set; }
        public string? Reason { get; set; }
        /// <summary>
        /// null (can delete) | staff_account | active_booking | pending_booking | unpaid_invoice | not_found | unavailable
        /// (the first reason; the counts below give all of them).
        /// </summary>
        public string? ReasonCode { get; set; }
        /// <summary>A customer account (role "general"): the only accounts that can delete themselves.</summary>
        public bool IsCustomerAccount { get; set; }
        public int ActiveBookings { get; set; }
        public int PendingBookings { get; set; }
        public int UnpaidInvoices { get; set; }
        public decimal BalanceDue { get; set; }
        /// <summary>The database script this needs before it can work ("QA_04_account_delete"), otherwise null.</summary>
        public string? NeedsDatabaseUpdate { get; set; }
    }

    /// <summary>The one row dbo.WN_Account_Delete returns (script QA_04_account_delete).</summary>
    public sealed class AccountDeletionRow
    {
        public int UserId { get; set; }
        public bool CanDelete { get; set; }
        public bool Deleted { get; set; }
        public string? ReasonCode { get; set; }
        public int ActiveBookings { get; set; }
        public int PendingBookings { get; set; }
        public int UnpaidInvoices { get; set; }
        public decimal BalanceDue { get; set; }
    }

    /// <summary>What the controller answers: HTTP status, message and the check (null for a 400 / 500).</summary>
    public sealed class AccountDeletionOutcome
    {
        public int StatusCode { get; init; }
        public bool IsSuccessful => StatusCode is >= 200 and < 300;
        public string Message { get; init; } = "";
        public AccountDeletionCheck? Check { get; init; }
        /// <summary>True only after the account was deleted (the caller then signs the token out).</summary>
        public bool Deleted { get; init; }
    }
}
