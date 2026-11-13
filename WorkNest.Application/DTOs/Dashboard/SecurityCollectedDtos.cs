namespace WorkNest.Application.DTOs.Dashboard
{
    /// <summary>
    /// GET api/dashboard/security-collected: security-deposit money actually received in the dashboard's period
    /// (the "Security collected" card and its list). Received = what the CEO dashboard's money received counts, deposit part
    /// only: an active fee receipt's SecurityDepositAmount (at most its amount) by ReceiptDate, or a paid payment's
    /// share of its invoice's deposit by its Pakistan paid day (a payment of an invoice that has an active receipt counts once, as the receipt).
    /// </summary>
    public class DashboardSecurityCollectedDto
    {
        /// <summary>month | quarter | year | custom</summary>
        public string Period { get; set; } = "month";
        /// <summary>First and last day of the period, yyyy-MM-dd (To = the last day counted: today for a calendar period).</summary>
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        /// <summary>The previous equal period (the overview's comparison), yyyy-MM-dd.</summary>
        public string PrevFrom { get; set; } = string.Empty;
        public string PrevTo { get; set; } = string.Empty;
        /// <summary>Deposit money received in the period (the card's value; the sum of every row).</summary>
        public decimal Total { get; set; }
        /// <summary>Receipts / payments that carried deposit money.</summary>
        public int Payments { get; set; }
        /// <summary>Distinct customers who paid it.</summary>
        public int Customers { get; set; }
        public decimal PrevTotal { get; set; }
        public int PrevPayments { get; set; }
        /// <summary>Business (Pakistan) time the figures were taken at, yyyy-MM-ddTHH:mm:ss.</summary>
        public string GeneratedAt { get; set; } = string.Empty;
        /// <summary>Newest first (the first <see cref="MaxRows"/>; Total / Payments count every match).</summary>
        public List<DashboardSecurityCollectedRowDto> Rows { get; set; } = new();

        public const int MaxRows = 1000;
    }

    public class DashboardSecurityCollectedRowDto
    {
        /// <summary>receipt (WN_FeeReceipts) | payment (WN_Payments).</summary>
        public string Kind { get; set; } = string.Empty;
        public int RefId { get; set; }
        /// <summary>Date received, yyyy-MM-dd (Pakistan day).</summary>
        public string ReceivedOn { get; set; } = string.Empty;
        public string Customer { get; set; } = string.Empty;
        public string? Company { get; set; }
        public int? BookingId { get; set; }
        public string? Space { get; set; }
        public int? LocationId { get; set; }
        public string? Location { get; set; }
        /// <summary>Receipt number (a payment: its transaction reference).</summary>
        public string? ReceiptNumber { get; set; }
        /// <summary>Voucher number the receipt was posted to (null: none yet / a payment).</summary>
        public string? VoucherNumber { get; set; }
        public int? InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        /// <summary>The booking's challan number, when it has one.</summary>
        public string? ChallanNumber { get; set; }
        /// <summary>The deposit money of this receipt / payment.</summary>
        public decimal Amount { get; set; }
    }
}
