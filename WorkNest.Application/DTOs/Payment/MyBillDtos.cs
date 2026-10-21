using System;
using System.Collections.Generic;

namespace WorkNest.Application.DTOs.Payment
{
    /// <summary>
    /// One bill on the customer's Payments tab (GET api/invoice/my): an invoice from WN_Invoices, or the booking
    /// challan of a booking that has no invoice yet. Amounts are the database's own; nothing is estimated.
    /// </summary>
    public class MyBillDto
    {
        /// <summary>"invoice" (PDF: GET api/invoice/{invoiceId}/pdf) or "challan" (GET api/booking/{bookingId}/challan).</summary>
        public string Kind { get; set; } = "invoice";
        public int? InvoiceId { get; set; }
        public Guid? InvoicePublicId { get; set; }
        /// <summary>Invoice number (INV-...) or challan number (WN-...).</summary>
        public string Number { get; set; } = string.Empty;
        /// <summary>Plain description: "Invoice", "Rent invoice" or "Booking challan".</summary>
        public string Title { get; set; } = string.Empty;
        public int? BookingId { get; set; }
        /// <summary>Booking status label from WN_BookingStatuses (Pending, Confirmed, Rejected...).</summary>
        public string? BookingStatus { get; set; }
        /// <summary>The booking will not go ahead (rejected, cancelled or no show).</summary>
        public bool BookingClosed { get; set; }
        public string? SpaceName { get; set; }
        public string? SpaceCode { get; set; }
        public string? LocationName { get; set; }
        /// <summary>Pakistan date the bill was issued.</summary>
        public DateTime? IssuedOn { get; set; }
        /// <summary>Last day to pay (invoice due date, or the challan's valid-until date).</summary>
        public DateTime? DueOn { get; set; }
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public string CurrencyCode { get; set; } = "PKR";
        /// <summary>Invoice grand total / challan total payable.</summary>
        public decimal Total { get; set; }
        /// <summary>Withholding tax the customer deducts (WHT invoices); not paid to WorkNest.</summary>
        public decimal TaxWithheld { get; set; }
        public decimal Paid { get; set; }
        /// <summary>Still to pay: Total - TaxWithheld - Paid, never below zero (0 for a cancelled bill).</summary>
        public decimal Balance { get; set; }
        /// <summary>Paid, Unpaid, Partially paid, Overdue, Expired or Cancelled.</summary>
        public string Status { get; set; } = "Unpaid";
        public bool IsOverdue { get; set; }
        public int DaysOverdue { get; set; }
        /// <summary>
        /// Door access to the booking's space is suspended from this Pakistan date if the bill is still unpaid
        /// (same rule as WN_HIK_AccessSuspension_Run: more than 7 days after the due date). Null when it doesn't apply.
        /// </summary>
        public DateTime? AccessSuspendsOn { get; set; }
        /// <summary>This bill is why door access to the booking's space is suspended right now.</summary>
        public bool AccessSuspended { get; set; }
        public bool HasPdf { get; set; }
        /// <summary>1-Link consumer number to pay this invoice with 1Bill (unpaid invoices of One Bill locations only).</summary>
        public string? OneBillNumber { get; set; }
    }

    public class MyBillsSummaryDto
    {
        /// <summary>Everything recorded as paid on the customer's bills.</summary>
        public decimal PaidTotal { get; set; }
        /// <summary>Still to pay on open bills (unpaid, partially paid and overdue).</summary>
        public decimal DueTotal { get; set; }
        public decimal OverdueTotal { get; set; }
        public int OverdueCount { get; set; }
        public int Count { get; set; }
    }

    public class MyBillsDto
    {
        public List<MyBillDto> Items { get; set; } = new();
        public MyBillsSummaryDto Summary { get; set; } = new();
    }

    /// <summary>An open door-access suspension of a booking (WN_HIK_BookingAccessSuspensions), in force today.</summary>
    public class BookingAccessSuspensionInfo
    {
        public int BookingId { get; set; }
        public int? InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime? InvoiceDueOn { get; set; }
        public DateTime? SuspendedAt { get; set; }
    }
}
