using WorkNest.Common.Responses;

namespace WorkNest.Application.DTOs.CustomInvoice
{
    // ---- Requests (CMS -> API) -----------------------------------------------------------------------------------

    /// <summary>
    /// POST api/custom-invoices: a one-off invoice with its own voucher, written by a super admin (or an admin given the
    /// CustomInvoice permission). Dates yyyy-MM-dd. Amounts in PKR, 0 or more. <see cref="Lines"/> are the voucher
    /// lines (2 to 5, each a debit or a credit, together balanced).
    /// </summary>
    public class CustomInvoiceCreateRequest
    {
        /// <summary>The customer (WN_Users.Id).</summary>
        public int? UserId { get; set; }
        /// <summary>The booking the invoice is for; null = none (then <see cref="LocationId"/> is required).</summary>
        public int? BookingId { get; set; }
        public int? LocationId { get; set; }
        /// <summary>An invoice of the booking this one replaces (it is cancelled and its voucher reversed); null = none.</summary>
        public int? ReplaceInvoiceId { get; set; }
        public DateTime? IssuedOn { get; set; }
        public DateTime? DueOn { get; set; }
        /// <summary>Billing period: both dates or neither (no period).</summary>
        public DateTime? PeriodFrom { get; set; }
        public DateTime? PeriodTo { get; set; }
        /// <summary>Room rent, excluding tax.</summary>
        public decimal? RentAmount { get; set; }
        public decimal? ServiceCharge { get; set; }
        /// <summary>Tax rate on the service charge, in percent (0-100).</summary>
        public decimal? TaxPercent { get; set; }
        /// <summary>Tax on the service charge.</summary>
        public decimal? TaxAmount { get; set; }
        public decimal? SecurityDeposit { get; set; }
        /// <summary>Must equal rent + service charge + tax + security deposit (to 2 decimals).</summary>
        public decimal? GrandTotal { get; set; }
        /// <summary>Why this invoice was written by hand. Required, at most 500 characters.</summary>
        public string? Reason { get; set; }
        public CustomInvoiceVoucherRequest? Voucher { get; set; }
        public CustomInvoiceCustomerLedgerRequest? CustomerLedger { get; set; }
        public List<CustomInvoiceLineRequest>? Lines { get; set; }
        /// <summary>True = post even though the customer ledger debit differs from the invoice total ("Post anyway").</summary>
        public bool OverrideTotalMismatch { get; set; }
    }

    public class CustomInvoiceVoucherRequest
    {
        /// <summary>The voucher date; the issue date when missing.</summary>
        public DateTime? Date { get; set; }
        /// <summary>At most 500 characters; the reason when empty.</summary>
        public string? Description { get; set; }
    }

    public class CustomInvoiceCustomerLedgerRequest
    {
        /// <summary>What the customer's ledger is debited with (normally the invoice total).</summary>
        public decimal? Debit { get; set; }
        public string? Narration { get; set; }
    }

    public class CustomInvoiceLineRequest
    {
        /// <summary>The account (AccountsCOA).</summary>
        public int? AccountId { get; set; }
        public decimal? Debit { get; set; }
        public decimal? Credit { get; set; }
        public string? Narration { get; set; }
    }

    /// <summary>POST api/custom-invoices/access/{userId}: { "hasAccess": true | false }.</summary>
    public class CustomInvoiceAccessRequest
    {
        public bool? HasAccess { get; set; }
    }

    // ---- What dbo.WN_CustomInvoice_Insert is called with (checked by CustomInvoiceService.Validate) ---------------

    public sealed class CustomInvoiceInsert
    {
        public int UserId { get; init; }
        public int? BookingId { get; init; }
        public int? LocationId { get; init; }
        public DateTime IssuedOn { get; init; }
        public DateTime DueOn { get; init; }
        /// <summary>Billing period: both dates or neither.</summary>
        public DateTime? PeriodFrom { get; init; }
        public DateTime? PeriodTo { get; init; }
        public decimal RoomRentExclTax { get; init; }
        public decimal ServiceCharges { get; init; }
        /// <summary>Null when the CMS sent none.</summary>
        public decimal? TaxPercent { get; init; }
        public decimal TaxOnServiceCharges { get; init; }
        public decimal SecurityDepositAmount { get; init; }
        public decimal GrandTotal { get; init; }
        public string CustomReason { get; init; } = string.Empty;
        public int CreatedById { get; set; }
        public DateTime VoucherDate { get; init; }
        public string? VoucherDescription { get; init; }
        public decimal CustomerLedgerDebit { get; init; }
        public string? CustomerLedgerNarration { get; init; }
        public int? ReplaceInvoiceId { get; init; }
        public List<CustomInvoiceInsertLine> Lines { get; init; } = new();
    }

    /// <summary>One row of the table type dbo.TT_WN_CustomInvoiceLedgerLine.</summary>
    public sealed record CustomInvoiceInsertLine(int LineNo, int AccountId, decimal Debit, decimal Credit, string? Narration);

    // ---- Responses (API -> CMS); dates are yyyy-MM-dd text -------------------------------------------------------

    public sealed class CustomInvoiceCreated
    {
        public int InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? PublicId { get; set; }
        public int? VoucherId { get; set; }
        public string? VoucherCode { get; set; }
        /// <summary>The invoice it replaced (cancelled), when one was chosen.</summary>
        public string? ReplacedInvoiceNumber { get; set; }
        /// <summary>The voucher that reversed the replaced invoice's voucher.</summary>
        public string? ReversalVoucherCode { get; set; }
    }

    public sealed class CustomInvoiceCustomer
    {
        public int UserId { get; set; }
        public int? CustomerId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Code { get; set; }
        public string? Phone { get; set; }
    }

    public sealed class CustomInvoiceBooking
    {
        public int BookingId { get; set; }
        public string? Label { get; set; }
        public string? SpaceName { get; set; }
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        public string? StartOn { get; set; }
        public string? EndOn { get; set; }
        public string? Status { get; set; }
    }

    public sealed class CustomInvoiceReplaceable
    {
        public int InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? IssuedOn { get; set; }
        public string? PeriodStart { get; set; }
        public string? PeriodEnd { get; set; }
        public decimal GrandTotal { get; set; }
        public int InvoiceTypeId { get; set; }
    }

    public sealed record CustomInvoiceLocation(int Id, string? Name);

    public sealed class CustomInvoiceAccount
    {
        public int AccountId { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
    }

    /// <summary>The location's accounts for a new invoice; each null when the location has none mapped.</summary>
    public sealed class CustomInvoiceLedgerDefaults
    {
        public CustomInvoiceAccount? AccountReceivable { get; set; }
        public CustomInvoiceAccount? Rent { get; set; }
        public CustomInvoiceAccount? ServicesIncome { get; set; }
        public CustomInvoiceAccount? SalesTax { get; set; }
        public CustomInvoiceAccount? SecurityReceived { get; set; }
    }

    public sealed class CustomInvoiceListItem
    {
        public int InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? CustomerName { get; set; }
        public string? BookingLabel { get; set; }
        public decimal GrandTotal { get; set; }
        public string? IssuedOn { get; set; }
        public string? StatusLabel { get; set; }
        public string? Reason { get; set; }
        public string? CreatedBy { get; set; }
        public string? VoucherCode { get; set; }
        public string? ReplacedInvoiceNumber { get; set; }
    }

    public sealed class CustomInvoiceListPage
    {
        public List<CustomInvoiceListItem> Items { get; set; } = new();
        public int Total { get; set; }
    }

    /// <summary>An admin on the Custom Invoice access list (super admin page).</summary>
    public sealed class CustomInvoiceAccessItem
    {
        public int UserId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string[] LocationNames { get; set; } = Array.Empty<string>();
        public bool HasAccess { get; set; }
        public string? GrantedBy { get; set; }
        public DateTime? GrantedAt { get; set; }
    }

    /// <summary>GET api/custom-invoices/access/me.</summary>
    public sealed class CustomInvoiceAccessMe
    {
        public bool CanUse { get; set; }
        public bool IsSuperAdmin { get; set; }
    }

    /// <summary>Who is asking, from the JWT (roles worked out by the API).</summary>
    public sealed record CustomInvoiceCaller(bool IsSuperAdmin, bool IsAdmin, string? Email);

    /// <summary>
    /// The locations the caller works with: every location for a super admin (<see cref="LocationIds"/> null); an admin
    /// only their own (JWT location_id + location_ids; none = nothing). The lookup procedures take no location filter,
    /// so CustomInvoiceService filters their rows with this.
    /// </summary>
    public sealed class CustomInvoiceScope
    {
        public static readonly CustomInvoiceScope All = new(null);

        private CustomInvoiceScope(IReadOnlyCollection<int>? locationIds) => LocationIds = locationIds;

        /// <summary>Null = every location.</summary>
        public IReadOnlyCollection<int>? LocationIds { get; }

        public bool IsAll => LocationIds == null;

        public static CustomInvoiceScope Of(IEnumerable<int>? locationIds) =>
            new((locationIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToList());

        public bool Allows(int? locationId) => IsAll || (locationId is > 0 && LocationIds!.Contains(locationId.Value));

        public bool AllowsAny(IEnumerable<int> locationIds) => IsAll || locationIds.Any(id => LocationIds!.Contains(id));
    }

    /// <summary>How a custom invoice call ended: the HTTP status and the { isSuccessful, message, data } body.</summary>
    public sealed class CustomInvoiceResult
    {
        public int StatusCode { get; init; } = 200;
        public ApiResponse Response { get; init; } = ApiResponse.Ok();

        public static CustomInvoiceResult Ok(object? data, string message = "Success") =>
            new() { StatusCode = 200, Response = ApiResponse.Ok(data, message) };

        public static CustomInvoiceResult BadRequest(string message) =>
            new() { StatusCode = 400, Response = ApiResponse.Fail(message) };

        public static CustomInvoiceResult Forbidden(string message) =>
            new() { StatusCode = 403, Response = ApiResponse.Fail(message) };

        public static CustomInvoiceResult NotFound(string message) =>
            new() { StatusCode = 404, Response = ApiResponse.Fail(message) };

        /// <summary>503: the database update CI_01_custom_invoice has not run.</summary>
        public static CustomInvoiceResult DatabaseUpdateNeeded() =>
            new() { StatusCode = 503, Response = ApiResponse.Fail(WorkNest.Application.Interfaces.CustomInvoiceDatabaseMissingException.UserMessage) };
    }
}
