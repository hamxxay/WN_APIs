using WorkNest.Application.DTOs.RentSchedule;
using WorkNest.Application.Services.BookingSchedules;

namespace WorkNest.Application.Interfaces
{
    /// <summary>What the schedule needs to know about a booking (dbo.WN_GetBookingInvoiceDetails, the same source the invoices use).</summary>
    public sealed class BookingBillingInfo
    {
        public int BookingId { get; set; }
        public int UserId { get; set; }
        public DateTime? StartOn { get; set; }
        public DateTime? EndOn { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal SubtotalAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal DiscountPercentage { get; set; }
        public int BillingPeriodMonths { get; set; } = 3;
        public decimal SecurityDepositRequired { get; set; }
        public decimal ResolvedSecurityDeposit { get; set; }
        public int Seats { get; set; } = 1;
        public decimal SupportRate { get; set; } = 2000m;
        public decimal TaxPercent { get; set; } = 16m;
        public string? SpaceName { get; set; }
        public string? SpaceTypeName { get; set; }
        public string? CategoryCode { get; set; }
        public string? CustomerName { get; set; }
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        // ledger accounts of the booking (as the invoices pass them)
        public int? RentAccountId { get; set; }
        public int? SecurityReceivedId { get; set; }
        public int? ServicesIncomeId { get; set; }
        public int? SalesTaxId { get; set; }
        public int? AccountReceivableId { get; set; }

        /// <summary>Meeting / conference rooms have no schedule (same test as the invoice code).</summary>
        public bool IsMeetingRoom =>
            (SpaceTypeName ?? "").Contains("Meeting", StringComparison.OrdinalIgnoreCase)
            || (SpaceTypeName ?? "").Contains("Conference", StringComparison.OrdinalIgnoreCase)
            || (CategoryCode ?? "").Contains("Meeting", StringComparison.OrdinalIgnoreCase)
            || (SpaceName ?? "").Contains("Meeting", StringComparison.OrdinalIgnoreCase)
            || (SpaceName ?? "").Contains("Conference", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A row of dbo.WN_BookingScheduleChanges (a sales executive's change waiting for / decided by the sales manager).</summary>
    public sealed class BookingScheduleChange
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public int RequestedById { get; set; }
        public string? RequestedByName { get; set; }
        public int? RequestedByManagerId { get; set; }
        public DateTime RequestedAt { get; set; }
        public string Status { get; set; } = "Pending";
        public string RowsJson { get; set; } = "[]";
        public int? DecidedById { get; set; }
        public string? DecidedByName { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? Note { get; set; }
        public string? CustomerName { get; set; }
        public string? SpaceName { get; set; }
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
    }

    /// <summary>
    /// dbo.WN_BookingSchedule / dbo.WN_BookingScheduleChanges (script CQ_01_rent_schedule): a booking's month-by-month rent table and the
    /// changes waiting for approval. <see cref="IsSetUpAsync"/> asks the catalog (OBJECT_ID) once; a yes is kept, a no is re-checked every minute.
    /// A row counts as invoiced while its InvoiceId points at an invoice that is not cancelled / deleted (StatusId 5, 40, 86).
    /// </summary>
    public interface IRentScheduleBookingRepository
    {
        Task<bool> IsSetUpAsync(CancellationToken ct = default);

        Task<BookingBillingInfo?> GetBillingInfoAsync(int bookingId, CancellationToken ct = default);

        /// <summary>The invoices already made for the booking that carry a billing period (same filter as dbo.WN_GetBookingLastInvoicePeriodEnd).</summary>
        Task<IReadOnlyList<InvoicedPeriod>> GetInvoicedPeriodsAsync(int bookingId, CancellationToken ct = default);

        /// <summary>The booking's stored rows in row order (Locked / InvoiceId / InvoiceNumber filled for invoiced rows); empty = no table.</summary>
        Task<IReadOnlyList<BookingScheduleRow>> GetRowsAsync(int bookingId, CancellationToken ct = default);

        /// <summary>The live invoice (not cancelled / deleted, any type but surcharge / custom-replace) that already covers exactly this period of the booking; null when none.</summary>
        Task<int?> FindLiveInvoiceForPeriodAsync(int bookingId, DateTime start, DateTime end, CancellationToken ct = default);

        /// <summary>
        /// Heals a booking whose conversion could not copy the quotation's rows: when the booking has none but its quotation has, copies them now.
        /// Throws when the copy fails (the invoice paths then fail loudly instead of billing from the old engine). Returns the rows copied.
        /// </summary>
        Task<int> EnsureBookingScheduleFromQuotationAsync(int bookingId, CancellationToken ct = default);

        Task<bool> HasRowsAsync(int bookingId, CancellationToken ct = default);

        /// <summary>Inserts the rows that are not stored yet and updates the un-invoiced stored ones (never an invoiced row), in one transaction.</summary>
        Task SaveRowsAsync(int bookingId, IReadOnlyList<BookingScheduleRow> rows, CancellationToken ct = default);

        /// <summary>Stores the invoice on the rows (only rows that are not invoiced); false when any of them was already taken (nothing is written then).</summary>
        Task<bool> MarkRowsInvoicedAsync(int bookingId, IReadOnlyCollection<int> rowNos, int invoiceId, CancellationToken ct = default);

        /// <summary>
        /// Copies the quotation's rows (WN_QuotationSchedule) to the booking (WN_BookingSchedule) right after the conversion. Does nothing when
        /// the quotation has no rows or the booking has some already; returns the number of rows copied.
        /// </summary>
        Task<int> CopyQuotationScheduleToBookingAsync(int quotationId, int bookingId, CancellationToken ct = default);

        Task<BookingScheduleChange?> GetPendingChangeAsync(int bookingId, CancellationToken ct = default);
        Task<BookingScheduleChange?> GetChangeAsync(int changeId, CancellationToken ct = default);
        /// <summary>Changes for the queue: a manager (<paramref name="managerUserId"/> set) sees those of their executives and of their locations; null = everything.</summary>
        Task<IReadOnlyList<BookingScheduleChange>> ListChangesAsync(bool pendingOnly, int? managerUserId, IReadOnlyCollection<int>? locationIds, CancellationToken ct = default);
        /// <summary>Monthly-space bookings (no meeting / conference rooms, not rejected / cancelled / deleted), newest first, at most 500.
        /// <paramref name="locationIds"/> null = every location; empty = none. <paramref name="activeOnly"/>: contracts not ended yet.</summary>
        Task<IReadOnlyList<BookingScheduleListItem>> ListBookingsAsync(string? search, IReadOnlyCollection<int>? locationIds, bool activeOnly, CancellationToken ct = default);
        /// <summary>Adds a Pending change; returns null when the booking already has one (one pending change per booking).</summary>
        Task<int?> InsertChangeAsync(int bookingId, int requestedById, string rowsJson, CancellationToken ct = default);
        /// <summary>Decides a Pending change (Approved / SentBack); false when it is no longer Pending.</summary>
        Task<bool> DecideChangeAsync(int changeId, string status, int decidedById, string? note, string? rowsJson, CancellationToken ct = default);
    }

    public sealed class BookingScheduleNotSetUpException : Exception
    {
        public const string Text = "Rent schedule is not set up on this server yet.";
        public BookingScheduleNotSetUpException() : base(Text) { }
    }
    public sealed class BookingScheduleNotFoundException : Exception
    {
        public BookingScheduleNotFoundException(string message = "Booking not found.") : base(message) { }
    }
    public sealed class BookingScheduleForbiddenException : Exception
    {
        public BookingScheduleForbiddenException(string message) : base(message) { }
    }
    public sealed class BookingScheduleRuleException : Exception
    {
        public BookingScheduleRuleException(string message) : base(message) { }
    }
    public sealed class BookingScheduleConflictException : Exception
    {
        public BookingScheduleConflictException(string message) : base(message) { }
    }
}
