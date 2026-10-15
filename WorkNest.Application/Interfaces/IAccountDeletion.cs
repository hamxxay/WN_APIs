using WorkNest.Application.DTOs.Account;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// dbo.WN_Account_Delete (script QA_04_account_delete): checks, and unless <c>checkOnly</c>, deletes (anonymises)
    /// a customer account in one transaction. Before QA_04 has run (SQL error 2812) it throws
    /// <see cref="AccountDeletionUnavailableException"/>.
    /// </summary>
    public interface IAccountDeletionRepository
    {
        Task<AccountDeletionRow> RunAsync(int userId, bool checkOnly, DateTime today, CancellationToken ct = default);
    }

    /// <summary>The database update QA_04_account_delete has not run: dbo.WN_Account_Delete is missing.</summary>
    public sealed class AccountDeletionUnavailableException : Exception
    {
        public AccountDeletionUnavailableException(Exception inner)
            : base("Deleting an account needs the database update QA_04_account_delete.", inner) { }
    }

    /// <summary>
    /// Self-service deletion of the signed-in user's own account (api/account/*). The user is always the one behind
    /// the sign-in token (its email, and its user id when the token has one): nobody can delete another account.
    /// </summary>
    public interface IAccountDeletionService
    {
        /// <summary>200 with the check (can delete, or why not). Nothing is changed.</summary>
        Task<AccountDeletionOutcome> CheckAsync(string? tokenEmail, string? tokenUserId, CancellationToken ct = default);

        /// <summary>
        /// 200 when deleted; 400 without the DELETE confirmation; 409 with the reason when refused (nothing changed);
        /// 503 before QA_04; 500 when the database failed (the transaction rolled back: nothing changed).
        /// </summary>
        Task<AccountDeletionOutcome> DeleteAsync(string? tokenEmail, string? tokenUserId, string? confirm, CancellationToken ct = default);
    }
}
