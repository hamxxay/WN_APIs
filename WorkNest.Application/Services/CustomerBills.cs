using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WorkNest.Application.DTOs.Payment;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Rules behind the customer's bills (GET api/invoice/my) and the door-access suspension notice. Pure functions,
    /// so the same rules can be unit tested.
    /// </summary>
    public static class CustomerBills
    {
        /// <summary>WN_HIK_AccessSuspension_Run suspends a booking's door access once an unpaid invoice's due date is more than this many days ago.</summary>
        public const int SuspensionGraceDays = 7;

        /// <summary>Amounts below one rupee are rounding left-overs, not money owed or paid.</summary>
        private const decimal Tolerance = 1m;

        public const string Paid = "Paid";
        public const string Unpaid = "Unpaid";
        public const string PartiallyPaid = "Partially paid";
        public const string Overdue = "Overdue";
        public const string Expired = "Expired";
        public const string Cancelled = "Cancelled";

        public sealed record BillState(string Status, bool IsVoid, bool IsOverdue, int DaysOverdue, decimal Balance);

        /// <summary>Status text of an invoice: legacy ids 1-5 first, then the dbo.OrderStatus description (same rule as the staff invoice lists).</summary>
        public static string? InvoiceStatusLabel(int statusId, string? orderStatusDescription)
        {
            switch (statusId)
            {
                case 1: return Unpaid;
                case 2: return Paid;
                case 3: return "Partial";
                case 4: return Overdue;
                case 5: return Cancelled;
            }
            var d = (orderStatusDescription ?? string.Empty).Trim();
            if (d.Length == 0) return null;
            if (d.Equals("Un Paid", StringComparison.OrdinalIgnoreCase)) return Unpaid;
            if (d.Equals("Challan Expire", StringComparison.OrdinalIgnoreCase)) return Overdue;
            return d;
        }

        /// <summary>A voided invoice: legacy 5 (Cancelled), 40 (Deleted) or OrderStatus 'Cancelled'/'Deleted'/'Delete'.</summary>
        public static bool IsVoidInvoice(int statusId, string? orderStatusDescription) =>
            statusId is 5 or 40 ||
            string.Equals((orderStatusDescription ?? string.Empty).Trim(), "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals((orderStatusDescription ?? string.Empty).Trim(), "Deleted", StringComparison.OrdinalIgnoreCase) ||
            string.Equals((orderStatusDescription ?? string.Empty).Trim(), "Delete", StringComparison.OrdinalIgnoreCase);

        /// <summary>Invoice statuses WN_HIK_AccessSuspension_Run treats as unpaid: legacy 1 / 4, 'Un Paid', 'Challan Expire'.</summary>
        public static bool CountsForSuspension(int statusId, string? orderStatusDescription)
        {
            if (IsVoidInvoice(statusId, orderStatusDescription)) return false;
            if (statusId is 1 or 4) return true;
            if (statusId is (>= 2 and <= 5) or 40) return false;
            var d = (orderStatusDescription ?? string.Empty).Trim();
            return d.Equals("Un Paid", StringComparison.OrdinalIgnoreCase) || d.Equals("Challan Expire", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>First Pakistan day on which door access is suspended if the invoice is still unpaid.</summary>
        public static DateTime AccessSuspendsOn(DateTime dueOn) => dueOn.Date.AddDays(SuspensionGraceDays + 1);

        /// <summary>Paid / Unpaid / Partially paid / Overdue / Cancelled for one invoice, with what is still to pay.</summary>
        public static BillState ClassifyInvoice(int statusId, string? orderStatusDescription, decimal total, decimal withheld,
            decimal paid, DateTime? dueOn, DateTime today)
        {
            if (IsVoidInvoice(statusId, orderStatusDescription)) return new BillState(Cancelled, true, false, 0, 0m);
            decimal balance = Math.Max(0m, total - withheld - paid);
            var label = InvoiceStatusLabel(statusId, orderStatusDescription);
            // The database marks it paid (fee receipt / bank reconciliation), or nothing is left to pay.
            if (string.Equals(label, Paid, StringComparison.OrdinalIgnoreCase) || balance < Tolerance)
                return new BillState(Paid, false, false, 0, 0m);
            int daysOverdue = dueOn.HasValue && dueOn.Value.Date < today.Date ? (today.Date - dueOn.Value.Date).Days : 0;
            if (daysOverdue > 0 || (!dueOn.HasValue && string.Equals(label, Overdue, StringComparison.OrdinalIgnoreCase)))
                return new BillState(Overdue, false, true, daysOverdue, balance);
            return new BillState(paid >= Tolerance ? PartiallyPaid : Unpaid, false, false, 0, balance);
        }

        /// <summary>
        /// The booking challan of a booking that has no invoice. A booking past Pending (confirmed) had its challan
        /// paid; a rejected / cancelled one is Cancelled unless money was received on it (then Paid: a refund case).
        /// </summary>
        public static BillState ClassifyChallan(bool bookingPending, bool bookingClosed, decimal total, decimal paid,
            DateTime? validUntil, DateTime now)
        {
            if (bookingClosed)
                return paid >= Tolerance ? new BillState(Paid, false, false, 0, 0m) : new BillState(Cancelled, true, false, 0, 0m);
            decimal balance = Math.Max(0m, total - paid);
            if (!bookingPending || balance < Tolerance) return new BillState(Paid, false, false, 0, 0m);
            if (IsChallanExpired(validUntil, now)) return new BillState(Expired, false, false, 0, balance);
            return new BillState(paid >= Tolerance ? PartiallyPaid : Unpaid, false, false, 0, balance);
        }

        /// <summary>A date without a time is valid until the end of that day; a date with a time until that moment.</summary>
        public static bool IsChallanExpired(DateTime? validUntil, DateTime now)
        {
            if (!validUntil.HasValue) return false;
            var v = validUntil.Value;
            var expiresAt = v.TimeOfDay == TimeSpan.Zero ? v.Date.AddDays(1) : v;
            return now >= expiresAt;
        }

        /// <summary>Rejected, cancelled or no show (status code or label from WN_BookingStatuses).</summary>
        public static bool IsClosedBooking(string? statusCode, string? statusLabel = null)
        {
            foreach (var raw in new[] { statusCode, statusLabel })
            {
                var k = Key(raw);
                if (k.Contains("reject") || k.Contains("cancel") || k.Contains("noshow")) return true;
            }
            return false;
        }

        /// <summary>Still waiting for its first payment (WN_BookingStatuses 'Pending', or the older status 1).</summary>
        public static bool IsPendingBooking(string? statusCode, string? statusLabel = null, int? statusId = null) =>
            statusId is 1 or 5 || Key(statusCode) == "pending" || Key(statusLabel) == "pending";

        private static string Key(string? value) =>
            new string((value ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        /// <summary>What the customer is told on the booking when its door access is suspended.</summary>
        public static string SuspensionMessage(string? invoiceNumber, DateTime? invoiceDueOn, string? challanNumber)
        {
            var en = CultureInfo.GetCultureInfo("en-GB");
            if (!string.IsNullOrWhiteSpace(invoiceNumber))
            {
                var due = invoiceDueOn.HasValue ? $" (it was due on {invoiceDueOn.Value.ToString("d MMM yyyy", en)})" : string.Empty;
                return $"Door access to this space is suspended because invoice {invoiceNumber.Trim()} is unpaid{due}. " +
                       "Access comes back once the payment is recorded. Contact WorkNest if you have already paid.";
            }
            var challan = string.IsNullOrWhiteSpace(challanNumber) ? "the booking challan" : $"booking challan {challanNumber.Trim()}";
            return $"Door access to this space is suspended because {challan} was not paid in time. Please contact WorkNest.";
        }

        /// <summary>Bills to pay first (overdue, then by due date), then the rest newest first.</summary>
        public static List<MyBillDto> Sort(IEnumerable<MyBillDto> bills)
        {
            static bool Open(MyBillDto b) => b.Balance >= Tolerance && (b.Status == Unpaid || b.Status == PartiallyPaid || b.Status == Overdue);
            return bills
                .OrderByDescending(Open)
                .ThenByDescending(b => Open(b) && b.IsOverdue)
                .ThenBy(b => Open(b) ? (b.DueOn ?? DateTime.MaxValue) : DateTime.MaxValue)
                .ThenByDescending(b => b.IssuedOn ?? DateTime.MinValue)
                .ThenByDescending(b => b.InvoiceId ?? 0)
                .ThenByDescending(b => b.BookingId ?? 0)
                .ToList();
        }

        public static MyBillsSummaryDto Summarize(IReadOnlyCollection<MyBillDto> bills)
        {
            var open = bills.Where(b => b.Status == Unpaid || b.Status == PartiallyPaid || b.Status == Overdue).ToList();
            return new MyBillsSummaryDto
            {
                PaidTotal = bills.Sum(b => b.Paid),
                DueTotal = open.Sum(b => b.Balance),
                OverdueTotal = open.Where(b => b.IsOverdue).Sum(b => b.Balance),
                OverdueCount = open.Count(b => b.IsOverdue),
                Count = bills.Count
            };
        }

        /// <summary>"Invoice", "Rent invoice" (recurring) — invoice type 2 is the recurring rent invoice.</summary>
        public static string InvoiceTitle(int? invoiceTypeId) => invoiceTypeId == 2 ? "Rent invoice" : "Invoice";
    }
}
