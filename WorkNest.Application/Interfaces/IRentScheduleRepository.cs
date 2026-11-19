using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WorkNest.Application.Interfaces
{
    /// <summary>One stored month of a rent schedule (dbo.WN_QuotationSchedule / dbo.WN_BookingSchedule; script CQ_01).</summary>
    public sealed class StoredScheduleRow
    {
        public int RowNo { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        /// <summary>decimal(9,6) as stored; the exact fraction is always n/30, see <see cref="Helpers.RentSchedule.ExactFraction"/>.</summary>
        public decimal MonthFraction { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal RoomRent { get; set; }
        public string DiscountType { get; set; } = "Percentage";
        public decimal DiscountValue { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal RentAfterDiscount { get; set; }
        public decimal Tax { get; set; }
        public decimal? SecurityDeposit { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>What a schedule edit writes onto the quotation header, so lists and dashboards see the table's totals.</summary>
    public sealed record QuotationScheduleHeader(decimal Subtotal, decimal DiscountAmount, decimal SecurityDeposit, decimal TotalAmount);

    /// <summary>
    /// Rent schedule storage (script CQ_01_rent_schedule): plain SQL on three new tables. <see cref="IsSetUpAsync"/> asks the catalog
    /// (a yes is kept, a no is re-checked every minute) so everything keeps today's behaviour on a server where the script has not run.
    /// </summary>
    public interface IRentScheduleRepository
    {
        /// <summary>True once CQ_01 has run: the three tables exist and the approval status check allows NotSubmitted.</summary>
        Task<bool> IsSetUpAsync(CancellationToken ct = default);

        /// <summary>The stored months of a quotation, by row number; empty = an old quotation without a schedule. (Part 2 reads this to copy the table onto the booking.)</summary>
        Task<IReadOnlyList<StoredScheduleRow>> GetQuotationRowsAsync(int quotationId, CancellationToken ct = default);

        /// <summary>Replaces a quotation's rows (all or nothing); with <paramref name="header"/> the quotation's totals are updated in the same transaction.</summary>
        Task ReplaceQuotationRowsAsync(int quotationId, IReadOnlyList<StoredScheduleRow> rows, QuotationScheduleHeader? header, CancellationToken ct = default);

        /// <summary>Copies the rows of one quotation onto another (a body-less new version). No rows = nothing copied.</summary>
        Task CopyQuotationRowsAsync(int sourceQuotationId, int newQuotationId, CancellationToken ct = default);
    }
}

namespace WorkNest.Application.Interfaces
{
    /// <summary>The script CQ_01 has not run: the schedule preview / booking schedule calls answer 503 with <see cref="Helpers.RentSchedule.NotSetUpMessage"/>.</summary>
    public sealed class RentScheduleNotSetUpException : Exception
    {
        public RentScheduleNotSetUpException() : base(Helpers.RentSchedule.NotSetUpMessage) { }
    }
}
