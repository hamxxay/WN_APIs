using WorkNest.Application.DTOs.RentSchedule;
using WorkNest.Application.Services.BookingSchedules;

namespace WorkNest.Application.Interfaces
{
    /// <summary>The per-booking billing lock (implemented in the API: a session application lock), taken while a table is saved so a save cannot race an invoice being built.</summary>
    public interface IRentScheduleGate
    {
        /// <summary>Disposable that releases the lock; throws <see cref="BookingScheduleConflictException"/> when another path holds it.</summary>
        Task<IAsyncDisposable> AcquireAsync(int bookingId, CancellationToken ct = default);
    }

    /// <summary>Who is calling (resolved by the controller from the JWT). A manager covers their own locations / executives; a super admin everything.</summary>
    public sealed record BookingScheduleCaller(int UserId, bool IsSuperAdmin, bool IsAdmin, bool IsSalesExecutive, bool IsSalesManager, IReadOnlyList<int>? LocationIds);

    /// <summary>The rows an invoice is made from, with the booking facts the invoice needs.</summary>
    public sealed class ScheduleInvoiceSource
    {
        public BookingBillingInfo Booking { get; init; } = new();
        public List<BookingScheduleRow> Rows { get; init; } = new();
        public ScheduleInvoiceFigures Figures { get; init; } = new();
        /// <summary>True when any month of the booking's table is invoiced already (the first invoice has been made).</summary>
        public bool AnyRowInvoiced { get; init; }
    }

    /// <summary>
    /// A booking's rent schedule: the table (stored, or projected for an old booking), saving it (admin / super admin directly, a sales
    /// executive through the sales manager), the manager's queue, and which rows the invoices bill. Before CQ_01 every call that needs the
    /// tables throws <see cref="BookingScheduleNotSetUpException"/>; the invoice helpers then answer null (old behaviour).
    /// </summary>
    public interface IBookingScheduleService
    {
        Task<bool> IsSetUpAsync(CancellationToken ct = default);

        Task<BookingScheduleResponse> GetAsync(int bookingId, CancellationToken ct = default);
        Task<SaveBookingScheduleResult> SaveAsync(BookingScheduleCaller caller, int bookingId, IReadOnlyList<BookingScheduleInputRow>? rows, CancellationToken ct = default);

        Task<IReadOnlyList<BookingScheduleChangeRowDto>> ListChangesAsync(BookingScheduleCaller caller, bool pendingOnly, CancellationToken ct = default);
        /// <summary>The bookings whose table the caller may open: super admin every location, everyone else their own locations.</summary>
        Task<IReadOnlyList<BookingScheduleListItem>> ListBookingsAsync(BookingScheduleCaller caller, string? search, bool activeOnly, CancellationToken ct = default);
        Task<BookingScheduleChangeDetailDto> GetChangeAsync(BookingScheduleCaller caller, int changeId, CancellationToken ct = default);
        Task<(string Message, BookingScheduleChangeDetailDto Detail)> ApproveAsync(BookingScheduleCaller caller, int changeId, IReadOnlyList<BookingScheduleInputRow>? editedRows, string? note, CancellationToken ct = default);
        Task<BookingScheduleChangeDetailDto> SendBackAsync(BookingScheduleCaller caller, int changeId, string? note, CancellationToken ct = default);

        /// <summary>
        /// The first invoice of a booking that has rows: the rows inside the first-invoice period. Null when the booking has no rows, CQ_01 has
        /// not run, something is invoiced already, or no row lies in the period (the caller then bills as it always did).
        /// </summary>
        Task<ScheduleInvoiceSource?> GetFirstInvoiceSourceAsync(int bookingId, CancellationToken ct = default);

        /// <summary>The next un-invoiced row of a booking that has rows (null when none / no table). <paramref name="hasSchedule"/> tells "no more months" from "no table".</summary>
        Task<(bool HasSchedule, ScheduleInvoiceSource? Next)> GetNextInvoiceSourceAsync(int bookingId, CancellationToken ct = default);

        /// <summary>The live invoice that already covers exactly this period of the booking (null when none).</summary>
        Task<int?> FindLiveInvoiceForPeriodAsync(int bookingId, DateTime start, DateTime end, CancellationToken ct = default);

        /// <summary>Stores the invoice on the rows (never a row that is already invoiced); false when a row was taken meanwhile.</summary>
        Task<bool> MarkInvoicedAsync(int bookingId, IReadOnlyCollection<int> rowNos, int invoiceId, CancellationToken ct = default);
    }
}
