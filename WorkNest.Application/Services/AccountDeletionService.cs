using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Account;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Self-service account deletion (Google Play rule, QA_04): a customer deletes their own account in the app
    /// (Profile > Delete account) or on worknestpk.com/delete-account. The account is the one behind the sign-in token:
    /// its email (and its user id when the token carries one, which must match). Staff, CEO, Sales Manager and Access
    /// Officer accounts are refused ("Ask a WorkNest admin"). The rules and the deletion itself are in
    /// dbo.WN_Account_Delete (one transaction: either everything or nothing is changed); before QA_04 has run the check
    /// answers "not available yet" and the deletion 503.
    /// </summary>
    public sealed class AccountDeletionService : IAccountDeletionService
    {
        private readonly IAccountDeletionRepository _repo;
        private readonly IDbRepository _db;
        private readonly IBusinessClock _clock;
        private readonly ILogger<AccountDeletionService> _logger;

        public AccountDeletionService(IAccountDeletionRepository repo, IDbRepository db, IBusinessClock clock,
            ILogger<AccountDeletionService> logger)
        {
            _repo = repo;
            _db = db;
            _clock = clock;
            _logger = logger;
        }

        public async Task<AccountDeletionOutcome> CheckAsync(string? tokenEmail, string? tokenUserId, CancellationToken ct = default)
        {
            var (account, refusal) = await ResolveAsync(tokenEmail, tokenUserId);
            if (refusal is not null)
                return refusal.StatusCode == 409 ? Ok(refusal.Check!) : refusal; // a check is an answer, not an error
            try
            {
                return Ok(AccountDeletionRules.FromRow(await _repo.RunAsync(account!.Value.UserId, checkOnly: true, _clock.Today, ct)));
            }
            catch (AccountDeletionUnavailableException ex)
            {
                _logger.LogWarning(ex, "Account deletion check: the database update QA_04_account_delete has not run.");
                return Ok(AccountDeletionRules.UnavailableCheck());
            }
        }

        public async Task<AccountDeletionOutcome> DeleteAsync(string? tokenEmail, string? tokenUserId, string? confirm, CancellationToken ct = default)
        {
            var (account, refusal) = await ResolveAsync(tokenEmail, tokenUserId);
            if (refusal is not null) return refusal;
            if (!AccountDeletionRules.IsConfirmed(confirm))
                return new AccountDeletionOutcome { StatusCode = 400, Message = AccountDeletionRules.ConfirmMessage };

            AccountDeletionRow row;
            try
            {
                // Not cancelled with the request: once sent, the transaction finishes (or rolls back) on its own.
                row = await _repo.RunAsync(account!.Value.UserId, checkOnly: false, _clock.Today, CancellationToken.None);
            }
            catch (AccountDeletionUnavailableException ex)
            {
                _logger.LogWarning(ex, "Account deletion refused: the database update QA_04_account_delete has not run.");
                var check = AccountDeletionRules.UnavailableCheck();
                return new AccountDeletionOutcome { StatusCode = 503, Message = check.Reason!, Check = check };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Account deletion failed for user {UserId}; the transaction was rolled back.", account!.Value.UserId);
                return new AccountDeletionOutcome { StatusCode = 500, Message = AccountDeletionRules.FailedMessage };
            }

            if (!row.Deleted)
            {
                var check = AccountDeletionRules.FromRow(row);
                return new AccountDeletionOutcome { StatusCode = 409, Message = check.Reason ?? AccountDeletionRules.FailedMessage, Check = check };
            }

            _logger.LogInformation("Account {UserId} was deleted by its owner (personal details removed, records kept).", row.UserId);
            return new AccountDeletionOutcome { StatusCode = 200, Message = AccountDeletionRules.DeletedMessage, Deleted = true };
        }

        /// <summary>
        /// The signed-in user's account: the active dbo.WN_Users row of the token's email (whose id must match the
        /// token's user id when it has a numeric one). Refusals: 401 no email, 403 the ids differ, 409 not found /
        /// not a customer account.
        /// </summary>
        private async Task<((int UserId, int? RoleId)? Account, AccountDeletionOutcome? Refusal)> ResolveAsync(string? tokenEmail, string? tokenUserId)
        {
            if (string.IsNullOrWhiteSpace(tokenEmail))
                return (null, new AccountDeletionOutcome { StatusCode = 401, Message = "Sign in required." });

            var row = await _db.GetUserByEmailAsync(tokenEmail.Trim());
            if (row is null || !row.TryGetValue("Id", out var idValue) || idValue is null
                || !int.TryParse(Convert.ToString(idValue, System.Globalization.CultureInfo.InvariantCulture), out var userId))
            {
                var missing = AccountDeletionRules.NotFoundCheck();
                return (null, new AccountDeletionOutcome { StatusCode = 409, Message = missing.Reason!, Check = missing });
            }

            if (int.TryParse(tokenUserId?.Trim(), out var tokenId) && tokenId > 0 && tokenId != userId)
            {
                _logger.LogWarning("Account deletion refused: token user id {TokenId} does not match the account {UserId} of its email.", tokenId, userId);
                return (null, new AccountDeletionOutcome { StatusCode = 403, Message = AccountDeletionRules.WrongAccountMessage });
            }

            var roleId = AccountDeletionRules.RoleIdOf(row);
            if (!AccountDeletionRules.IsCustomerRole(roleId))
            {
                var staff = AccountDeletionRules.StaffCheck();
                return (null, new AccountDeletionOutcome { StatusCode = 409, Message = staff.Reason!, Check = staff });
            }

            return ((userId, roleId), null);
        }

        private static AccountDeletionOutcome Ok(AccountDeletionCheck check) => new()
        {
            StatusCode = 200,
            Message = check.CanDelete ? AccountDeletionRules.CanDeleteMessage : check.Reason ?? "",
            Check = check
        };
    }
}
