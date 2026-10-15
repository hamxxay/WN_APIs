using System.Globalization;
using WorkNest.Application.DTOs.Account;
using WorkNest.Common.Constants;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Self-service account deletion (Google Play rule, QA_04): who may delete their own account, the confirmation
    /// word and the plain-language reasons. The procedure dbo.WN_Account_Delete applies the same rules again inside
    /// its transaction; this side explains them before anything is asked.
    /// </summary>
    public static class AccountDeletionRules
    {
        /// <summary>What the user types to confirm.</summary>
        public const string ConfirmWord = "DELETE";

        /// <summary>The database script the deletion needs.</summary>
        public const string DatabaseUpdate = "QA_04_account_delete";

        public const string NotFound = "not_found";
        public const string StaffAccount = "staff_account";
        public const string ActiveBooking = "active_booking";
        public const string PendingBooking = "pending_booking";
        public const string UnpaidInvoice = "unpaid_invoice";
        public const string Unavailable = "unavailable";

        public const string StaffMessage = "Staff accounts can't be deleted here. Ask a WorkNest admin to remove your account.";
        public const string NotFoundMessage = "This account no longer exists or has already been switched off.";
        public const string UnavailableMessage =
            "Deleting an account online isn't available yet. Please contact WorkNest and we will delete it for you.";
        public const string ConfirmMessage = "Type DELETE to confirm that you want to delete your account.";
        public const string DeletedMessage = "Your WorkNest account has been deleted.";
        public const string FailedMessage = "Your account could not be deleted. Nothing was changed. Please try again.";
        public const string CanDeleteMessage = "You can delete your account.";
        public const string WrongAccountMessage = "This sign-in does not match your account. Please sign in again.";

        /// <summary>The confirmation text is DELETE (spaces around it and letter case ignored).</summary>
        public static bool IsConfirmed(string? confirm) =>
            string.Equals(confirm?.Trim(), ConfirmWord, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// A customer account (role "general", or no role at all, as everywhere else in the API). Every other role id
        /// (staff, CEO, Sales Manager, Access Officer, or an unknown id) is removed by a WorkNest admin.
        /// </summary>
        public static bool IsCustomerRole(int? roleId) => (roleId ?? Roles.GeneralId) == Roles.GeneralId;

        /// <summary>
        /// The role id of a dbo.WN_Users row: the RoleId column (null when it is empty). A row without a readable RoleId
        /// is treated like a customer here; the procedure checks the stored RoleId again.
        /// </summary>
        public static int? RoleIdOf(IDictionary<string, object?>? row)
        {
            if (row is null || !row.TryGetValue("RoleId", out var value) || value is null) return null;
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var id) ? id : null;
        }

        /// <summary>The check for an account that is not a customer account.</summary>
        public static AccountDeletionCheck StaffCheck() => new()
        {
            CanDelete = false,
            ReasonCode = StaffAccount,
            Reason = StaffMessage,
            IsCustomerAccount = false
        };

        /// <summary>The check before the database update has run: nothing can be deleted online yet.</summary>
        public static AccountDeletionCheck UnavailableCheck() => new()
        {
            CanDelete = false,
            ReasonCode = Unavailable,
            Reason = UnavailableMessage,
            IsCustomerAccount = true,
            NeedsDatabaseUpdate = DatabaseUpdate
        };

        /// <summary>The check for a signed-in user whose account row was not found (or is switched off).</summary>
        public static AccountDeletionCheck NotFoundCheck() => new()
        {
            CanDelete = false,
            ReasonCode = NotFound,
            Reason = NotFoundMessage,
            IsCustomerAccount = false
        };

        /// <summary>The check from the procedure's row, with the reason written out.</summary>
        public static AccountDeletionCheck FromRow(AccountDeletionRow row)
        {
            var check = new AccountDeletionCheck
            {
                CanDelete = row.CanDelete && string.IsNullOrWhiteSpace(row.ReasonCode),
                ReasonCode = string.IsNullOrWhiteSpace(row.ReasonCode) ? null : row.ReasonCode.Trim(),
                ActiveBookings = Math.Max(0, row.ActiveBookings),
                PendingBookings = Math.Max(0, row.PendingBookings),
                UnpaidInvoices = Math.Max(0, row.UnpaidInvoices),
                BalanceDue = Math.Max(0, row.BalanceDue)
            };
            check.IsCustomerAccount = check.ReasonCode is not (StaffAccount or NotFound);
            check.Reason = check.CanDelete ? null : ReasonText(check);
            return check;
        }

        /// <summary>
        /// The reason as a sentence. Bookings and invoices are listed together, e.g. "You can't delete your account
        /// while you have an active booking and 2 unpaid invoices (PKR 12,000 due). ..."
        /// </summary>
        public static string ReasonText(AccountDeletionCheck check)
        {
            switch (check.ReasonCode)
            {
                case StaffAccount: return StaffMessage;
                case NotFound: return NotFoundMessage;
                case Unavailable: return UnavailableMessage;
            }

            var parts = new List<string>();
            if (check.ActiveBookings > 0)
                parts.Add(check.ActiveBookings == 1 ? "an active booking" : $"{check.ActiveBookings} active bookings");
            if (check.PendingBookings > 0)
                parts.Add(check.PendingBookings == 1
                    ? "a booking request waiting for confirmation"
                    : $"{check.PendingBookings} booking requests waiting for confirmation");
            if (check.UnpaidInvoices > 0)
            {
                var due = check.BalanceDue > 0 ? $" (PKR {Math.Round(check.BalanceDue, 0).ToString("N0", CultureInfo.InvariantCulture)} due)" : "";
                parts.Add((check.UnpaidInvoices == 1 ? "an unpaid invoice" : $"{check.UnpaidInvoices} unpaid invoices") + due);
            }
            if (parts.Count == 0)
                return "Your account can't be deleted right now. Please contact WorkNest for help.";

            return $"You can't delete your account while you have {JoinAnd(parts)}. " +
                   "Once your bookings have ended and everything is paid you can delete it here, or contact WorkNest for help.";
        }

        /// <summary>"a", "a and b", "a, b and c".</summary>
        public static string JoinAnd(IReadOnlyList<string> parts) => parts.Count switch
        {
            0 => "",
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1]
        };
    }
}
