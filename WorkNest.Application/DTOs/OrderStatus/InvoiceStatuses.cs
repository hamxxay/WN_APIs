namespace WorkNest.Application.DTOs.OrderStatus
{
    /// <summary>
    /// Invoice / payment status IDs resolved from dbo.OrderStatus by description (no hard-coded IDs),
    /// plus the legacy values (1 Unpaid, 2 Paid, 3 Partial, 4 Overdue, 5 Void) still found on older rows.
    /// </summary>
    public sealed class InvoiceStatuses
    {
        // OrderStatus.Description values used for invoices / challans.
        public const string UnpaidName = "Un Paid";
        public const string PaidName = "Paid";
        public const string PartialName = "Partial";
        public const string OverdueName = "Challan Expire";
        public const string CancelledName = "Cancelled";

        /// <summary>OrderStatus IDs — what new writes should use (null if the row is missing).</summary>
        public int? UnpaidId { get; init; }
        public int? PaidId { get; init; }
        public int? PartialId { get; init; }
        public int? OverdueId { get; init; }
        public int? CancelledId { get; init; }

        /// <summary>Every ID that means each state (OrderStatus ID + legacy value).</summary>
        public HashSet<int> Unpaid { get; init; } = new();
        public HashSet<int> Paid { get; init; } = new();
        public HashSet<int> Partial { get; init; } = new();
        public HashSet<int> Overdue { get; init; } = new();
        public HashSet<int> Cancelled { get; init; } = new();

        public bool IsUnpaid(int? id) => id.HasValue && Unpaid.Contains(id.Value);
        public bool IsPaid(int? id) => id.HasValue && Paid.Contains(id.Value);
        public bool IsPartial(int? id) => id.HasValue && Partial.Contains(id.Value);
        public bool IsOverdue(int? id) => id.HasValue && Overdue.Contains(id.Value);
        public bool IsCancelled(int? id) => id.HasValue && Cancelled.Contains(id.Value);

        /// <summary>Display label: Unpaid | Paid | Partial | Overdue | Cancelled.</summary>
        public string Label(int? id) =>
            IsPaid(id) ? "Paid"
            : IsPartial(id) ? "Partial"
            : IsOverdue(id) ? "Overdue"
            : IsCancelled(id) ? "Cancelled"
            : "Unpaid";
    }
}
