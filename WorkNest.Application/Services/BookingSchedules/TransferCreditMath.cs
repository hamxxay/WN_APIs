using WorkNest.Application.DTOs.RentSchedule;

namespace WorkNest.Application.Services.BookingSchedules
{
    /// <summary>
    /// TRF_02 transfer credit (WN_Bookings.TransferCreditBalance of the new booking), taken off the next invoices while they are created. Pure.
    /// Per invoice the credit is capped at its room rent excluding the service charge (RentAfterDiscount - ServiceCharge), never negative, and
    /// what is left of the balance carries forward to the next invoice. It travels as extra discount (Discount, RentAfterDiscount, RoomRentExclTax
    /// and GrandTotal all drop by it) so the invoice voucher still balances: AR debit = GrandTotal = rent + service + tax + deposit credits.
    /// </summary>
    public static class TransferCreditMath
    {
        /// <summary>The credit one invoice takes: min(balance, rentAfterDiscount - serviceCharge), at least 0, in cents.</summary>
        public static decimal Credit(decimal balance, decimal rentAfterDiscount, decimal serviceCharge)
        {
            if (balance <= 0m) return 0m;
            decimal cap = rentAfterDiscount - serviceCharge;
            if (cap <= 0m) return 0m;
            return BookingScheduleMath.Round2(Math.Min(balance, cap));
        }

        /// <summary>The balance after one invoice took <paramref name="credit"/> (never below 0).</summary>
        public static decimal Remaining(decimal balance, decimal credit) => Math.Max(0m, BookingScheduleMath.Round2(balance - credit));

        /// <summary>Takes the credit off the figures (once: a no-op when a credit is already applied or there is nothing to take). Returns the credit.</summary>
        public static decimal Apply(ScheduleInvoiceFigures f, decimal balance)
        {
            if (f.TransferCredit > 0m) return f.TransferCredit;
            decimal c = Credit(balance, f.RentAfterDiscount, f.ServiceCharge);
            if (c <= 0m) return 0m;
            f.TransferCredit = c;
            f.Discount = BookingScheduleMath.Round2(f.Discount + c);
            f.RentAfterDiscount = BookingScheduleMath.Round2(f.RentAfterDiscount - c);
            f.RoomRentExclTax = BookingScheduleMath.Round2(f.RoomRentExclTax - c);
            f.GrandTotal = BookingScheduleMath.Round2(f.GrandTotal - c);
            return c;
        }

        /// <summary>Splits an invoice's credit over its rows, in row order, each up to its own room rent (billed rent after discount - service charge).</summary>
        public static Dictionary<int, decimal> SplitOverRows(IReadOnlyList<BookingScheduleRow> rows, decimal credit)
        {
            var result = new Dictionary<int, decimal>();
            decimal left = credit;
            foreach (var r in rows.OrderBy(r => r.RowNo))
            {
                if (left <= 0m) break;
                var b = BookingScheduleMath.Billed(r);
                decimal room = Math.Max(0m, b.RentAfterDiscount - b.ServiceCharge);
                decimal take = Math.Min(left, room);
                if (take <= 0m) continue;
                result[r.RowNo] = BookingScheduleMath.Round2(take);
                left = BookingScheduleMath.Round2(left - take);
            }
            if (left > 0m && rows.Count > 0)
            {
                int last = rows.Max(r => r.RowNo);
                result[last] = BookingScheduleMath.Round2((result.TryGetValue(last, out var v) ? v : 0m) + left);
            }
            return result;
        }
    }
}
