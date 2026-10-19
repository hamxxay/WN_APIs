using WorkNest.Application.DTOs.CustomInvoice;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Custom invoices (CMS page Custom Invoice, api/custom-invoices/*): one-off invoices with their own voucher, written
    /// by a super admin or an admin given the CustomInvoice permission. Every call answers a <see cref="CustomInvoiceResult"/>:
    /// 400 for a broken rule (the API's checks, or a procedure's THROW 51000 message), 503 before the database update
    /// CI_01_custom_invoice has run. No edit or delete: a wrong invoice is replaced by a new one.
    /// </summary>
    /// <remarks>
    /// <see cref="CustomInvoiceScope"/>: a super admin sees every location; an admin only their own (customers with a
    /// booking or account there or with no location yet, bookings / invoices / locations there; 403 for another location).
    /// </remarks>
    public interface ICustomInvoiceService
    {
        Task<CustomInvoiceResult> SearchCustomersAsync(string? search, CustomInvoiceScope scope);
        Task<CustomInvoiceResult> GetBookingsAsync(int? userId, CustomInvoiceScope scope);
        Task<CustomInvoiceResult> GetReplaceableAsync(int? bookingId, CustomInvoiceScope scope);
        Task<CustomInvoiceResult> GetLocationsAsync(CustomInvoiceScope scope);
        /// <summary>The location's accounts on <paramref name="date"/> (today when null).</summary>
        Task<CustomInvoiceResult> GetLedgerDefaultsAsync(int? locationId, DateTime? date, CustomInvoiceScope scope);
        Task<CustomInvoiceResult> SearchAccountsAsync(string? search);
        /// <summary>
        /// Checks the request (<c>CustomInvoiceService.Validate</c>) and the booking / location against the scope (403),
        /// then writes the invoice and its voucher.
        /// </summary>
        Task<CustomInvoiceResult> CreateAsync(CustomInvoiceCreateRequest? request, int createdById, CustomInvoiceScope scope);
        Task<CustomInvoiceResult> GetListAsync(int page, string? search, CustomInvoiceScope scope, int pageSize = 20);
        /// <summary>Every active admin with whether they have the CustomInvoice permission (super admin page).</summary>
        Task<CustomInvoiceResult> ListAccessAsync();
        /// <summary>Gives or takes away the CustomInvoice permission; clears that user's cached access.</summary>
        Task<CustomInvoiceResult> SetAccessAsync(int userId, CustomInvoiceAccessRequest? request, int grantedById);
    }

    /// <summary>
    /// Who may use the custom invoice endpoints (policy "CustomInvoiceAccess"): a super admin, or an admin with the
    /// CustomInvoice permission (dbo.WN_UserPermissions_GetByUser, cached 30 seconds per user, cleared on a change).
    /// Before CI_01 (the procedure is missing) admins are refused and super admins allowed.
    /// </summary>
    public interface ICustomInvoiceAccessService
    {
        /// <summary>{ canUse, isSuperAdmin } for this caller. Never throws: a database failure refuses an admin.</summary>
        Task<CustomInvoiceAccessMe> CheckAsync(CustomInvoiceCaller caller);

        /// <summary>WN_Users.Id of the signed-in e-mail (dbo.WN_Users_GetByEmail, cached); null when there is none.</summary>
        Task<int?> ResolveUserIdAsync(string? email);

        /// <summary>Drops the user's cached permission, so a grant / removal applies on their next request.</summary>
        void Forget(int userId);
    }

    /// <summary>
    /// dbo.WN_CustomInvoice_* and dbo.WN_UserPermissions_* (script CI_01_custom_invoice). Writes set the audit
    /// SESSION_CONTEXT (AppUserId, AppUserEmail, SourceApp = 'WebAPI') from the JWT first, like DbRepository.Open().
    /// SQL errors 2812 / 2715 / 208 (procedure / table type / table missing) throw <see cref="CustomInvoiceDatabaseMissingException"/>;
    /// a procedure's THROW 51000 throws <see cref="CustomInvoiceRuleException"/> with its message.
    /// </summary>
    public interface ICustomInvoiceRepository
    {
        Task<IReadOnlyList<string>> GetUserPermissionsAsync(int userId);
        Task<IReadOnlyList<CustomInvoiceAccessItem>> ListAdminsAsync(string permission);
        /// <summary>Returns the admin as the list shows them (null when the procedure returns no row).</summary>
        Task<CustomInvoiceAccessItem?> SetPermissionAsync(int userId, string permission, bool grant, int grantedById);
        Task<IReadOnlyList<CustomInvoiceCustomer>> SearchCustomersAsync(string? search, int top);
        Task<IReadOnlyList<CustomInvoiceBooking>> GetBookingsAsync(int userId);
        Task<IReadOnlyList<CustomInvoiceReplaceable>> GetReplaceableAsync(int bookingId);
        Task<CustomInvoiceLedgerDefaults> GetLedgerDefaultsAsync(int locationId, DateTime date);
        Task<IReadOnlyList<CustomInvoiceAccount>> SearchAccountsAsync(string? search, int top);
        /// <summary>Writes the invoice, its voucher and ledger lines in one transaction (null when no row came back).</summary>
        Task<CustomInvoiceCreated?> InsertAsync(CustomInvoiceInsert invoice);
        Task<CustomInvoiceListPage> GetListAsync(string? search, int page, int pageSize);
        /// <summary>
        /// Location of each invoice (the custom invoice's chosen location, else its booking's); invoices with none are left out.
        /// </summary>
        Task<Dictionary<int, int>> GetInvoiceLocationsAsync(IReadOnlyCollection<int> invoiceIds);
        /// <summary>
        /// Locations of each customer (by UserId), as RecordScope / KYC count them: their account's location and the
        /// locations of their bookings (by user, or by the customer code). A customer with none is left out.
        /// </summary>
        Task<Dictionary<int, HashSet<int>>> GetCustomerLocationsAsync(IReadOnlyCollection<(int UserId, int? CustomerId)> customers);
    }

    /// <summary>SQL Server error numbers the custom invoice calls react to.</summary>
    public static class CustomInvoiceDbErrors
    {
        /// <summary>THROW 51000 in a CI_01 procedure: a business rule, its message is for the user (400).</summary>
        public const int Rule = 51000;

        /// <summary>2812 procedure missing, 2715 table type missing, 208 table missing: CI_01 has not run (503).</summary>
        public static bool IsMissingObject(int number) => number is 2812 or 2715 or 208;
    }

    /// <summary>The database update CI_01_custom_invoice has not run: no procedures / table type / tables.</summary>
    public sealed class CustomInvoiceDatabaseMissingException : Exception
    {
        /// <summary>What the CMS shows (503). No "sql" in it: the CMS hides messages containing that word.</summary>
        public const string UserMessage =
            "Database update needed: run the script CI_01_custom_invoice_TEST (CI_01_custom_invoice_LIVE on the live database).";

        public CustomInvoiceDatabaseMissingException(Exception? inner = null) : base(UserMessage, inner) { }
    }

    /// <summary>A rule broken in a CI_01 procedure (THROW 51000); the message is shown as it is (400).</summary>
    public sealed class CustomInvoiceRuleException : Exception
    {
        public CustomInvoiceRuleException(string message, Exception? inner = null) : base(message, inner) { }
    }
}
