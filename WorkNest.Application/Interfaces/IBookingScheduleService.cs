using WorkNest.Application.DTOs.RentSchedule;
using WorkNest.Application.Services.RentSchedule;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Who is calling (resolved by the controller from the JWT). A manager covers their own locations / executives; a super admin everything.</summary>
    public sealed record BookingScheduleCaller(int UserId, bool IsSuperAdmin, bool IsAdmin, bool IsSalesExecutive, bool IsSalesManager, IReadOnlyList<int>? LocationIds);

    /// <summary>The rows an invoice is made from, with the booking facts the invoice needs.</summary>
    public sealed class ScheduleInvoiceSource
    {
        public BookingBillingInfo Booking { get; init; } = new();
        public List<BookingScheduleRow> Rows { get; init; } = new();
        public ScheduleInvoiceFigures Figures { get; init; } = new();
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

        /// <summary>Stores the invoice on the rows (never a row that is already invoiced); false when a row was taken meanwhile.</summary>
        Task<bool> MarkInvoicedAsync(int bookingId, IReadOnlyCollection<int> rowNos, int invoiceId, CancellationToken ct = default);
    }
}
