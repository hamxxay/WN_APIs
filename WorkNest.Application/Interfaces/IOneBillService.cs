namespace WorkNest.Application.Interfaces
{
 
    public interface IOneBillService
    {

        Task<string?> EnsureAsync(int invoiceId, CancellationToken ct = default);

        /// <summary>The invoice's One Bill number once it exists on EzPay, else null. Never throws.</summary>
        Task<string?> GetNumberAsync(int invoiceId, CancellationToken ct = default);

        /// <summary>Invoices still to send to EzPay (dbo.WN_OneBill_GetPending), oldest first.</summary>
        Task<IReadOnlyList<int>> GetPendingAsync(int top, CancellationToken ct = default);

        /// <summary>OneBill:Enabled is true and OneBill:BaseUrl / Username / Password are set.</summary>
        bool IsConfigured { get; }

        /// <summary>Records an EzPay payment notification (dbo.WN_OneBill_RecordPayment) in one transaction, here and in the procedure.</summary>
        Task<OneBillPaymentResult> RecordPaymentAsync(OneBillPaymentNotice notice, CancellationToken ct = default);
    }

    /// <summary>A payment notification from EzPay, as received.</summary>
    public sealed class OneBillPaymentNotice
    {
        public string BillNo { get; init; } = "";
        public decimal Amount { get; init; }
        public DateTime? ReceivedDate { get; init; }
        public TimeSpan? ReceivedTime { get; init; }
        public string? ReceivedVia { get; init; }
        public string? RawBody { get; init; }
        public string? RemoteIp { get; init; }
    }


    public sealed class OneBillPaymentResult
    {
        public int NotificationId { get; init; }
        public string Outcome { get; init; } = "";
        public string? Message { get; init; }
        public int? InvoiceId { get; init; }
        public string? InvoiceNumber { get; init; }
        public decimal? PaidTotal { get; init; }
        public int? StatusId { get; init; }
    }
}
