using System.Globalization;
using WorkNest.Application.DTOs.RentSchedule;

namespace WorkNest.Application.Services.RentSchedule
{
    /// <summary>A month already billed on an invoice of the booking (WN_Invoices), as the schedule shows it (locked).</summary>
    public sealed class InvoicedPeriod
    {
        public int InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        /// <summary>Gross rent (WN_Invoices.SubTotal), before the discount.</summary>
        public decimal SubTotal { get; set; }
        public decimal DiscountTotal { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal ServiceCharges { get; set; }
        public decimal SecurityDeposit { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal Months { get; set; } = 1m;
    }

    /// <summary>Everything the projection of an old booking (no table yet) needs.</summary>
    public sealed class BookingProjectionInput
    {
        public DateTime ContractStart { get; set; }
        public DateTime ContractEnd { get; set; }
        /// <summary>The booking's monthly rent, service charge included (what the invoices charge per month).</summary>
        public decimal MonthlyRent { get; set; }
        public int Seats { get; set; } = 1;
        public decimal SupportRate { get; set; } = 2000m;
        public decimal TaxPercent { get; set; } = 16m;
        public decimal DiscountPercentage { get; set; }
        public decimal DiscountAmount { get; set; }
        public int BillingPeriodMonths { get; set; } = 3;
        /// <summary>Deposit of the first invoice (only used while nothing has been invoiced yet).</summary>
        public decimal InitialDeposit { get; set; }
        public IReadOnlyList<InvoicedPeriod> Invoiced { get; set; } = Array.Empty<InvoicedPeriod>();
    }

    /// <summary>What one invoice made from rows contains (sums of the rows).</summary>
    public sealed class ScheduleInvoiceFigures
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        /// <summary>Months covered (sum of the rows' fractions), at least 1 as a whole number for AdvanceRentMonths.</summary>
        public int Months { get; set; }
        public decimal MonthsExact { get; set; }
        /// <summary>Gross rent before the discount (sum of monthlyRent): invoice SubTotal.</summary>
        public decimal Rent { get; set; }
        public decimal Discount { get; set; }
        public decimal RentAfterDiscount { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal Tax { get; set; }
        public decimal Deposit { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal RoomRentExclTax { get; set; }
        public List<int> RowNos { get; set; } = new();
    }

    /// <summary>
    /// The maths of a booking's rent schedule (spec 2026-11-19, section 3), pure and static: the per-row amounts, the totals, the
    /// projection of an old booking (so the table equals what today's recurring invoice charges), which rows make the first
    /// invoice and which row is the next one.
    /// </summary>
    public static class BookingScheduleMath
    {
        public const string Percentage = "Percentage";
        public const string Amount = "Amount";
        /// <summary>Same 30-day month as InvoiceCalculationEngine.ProrationDaysPerMonth.</summary>
        public const int DaysPerMonth = InvoiceCalculationEngine.ProrationDaysPerMonth;

        public static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        /// <summary>"Percentage" / "Amount" (any case; "Fixed" counts as Amount); null when it is neither.</summary>
        public static string? NormalizeDiscountType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type)) return null;
            var t = type.Trim();
            if (t.Equals(Percentage, StringComparison.OrdinalIgnoreCase) || t.Equals("percent", StringComparison.OrdinalIgnoreCase)) return Percentage;
            if (t.Equals(Amount, StringComparison.OrdinalIgnoreCase) || t.Equals("Fixed", StringComparison.OrdinalIgnoreCase) || t.Equals("Flat", StringComparison.OrdinalIgnoreCase)) return Amount;
            return null;
        }

        // ---- per-row maths (spec section 3) -----------------------------------------------------------------------------------

        /// <summary>monthlyRent = round2(monthly x fraction); serviceCharge = round2(rate x seats x fraction), never above the rent.</summary>
        public static (decimal MonthlyRent, decimal ServiceCharge) BaseAmounts(decimal fullMonthlyRent, decimal supportRate, int seats, decimal fraction)
        {
            decimal rent = Round2(fullMonthlyRent * fraction);
            decimal service = Round2(supportRate * Math.Max(1, seats) * fraction);
            if (service > rent) service = rent;
            if (service < 0) service = 0;
            return (rent, service);
        }

        /// <summary>
        /// roomRent = max(0, monthlyRent - serviceCharge). Discount: Percentage = round2(roomRent x v / 100); Amount = min(v, roomRent);
        /// the service charge is never discounted. rentAfterDiscount = monthlyRent - discount; tax = round2(serviceCharge x taxPercent / 100);
        /// total = rentAfterDiscount + tax + deposit.
        /// </summary>
        public static void Recompute(BookingScheduleRow row, decimal taxPercent)
        {
            decimal roomRent = Math.Max(0m, row.MonthlyRent - row.ServiceCharge);
            var type = NormalizeDiscountType(row.DiscountType) ?? Amount;
            decimal v = Math.Max(0m, row.DiscountValue);
            decimal discount = type == Percentage
                ? Round2(roomRent * Math.Min(v, 100m) / 100m)
                : Round2(Math.Min(v, roomRent));
            row.DiscountType = type;
            row.RoomRent = roomRent;
            row.DiscountAmount = discount;
            row.RentAfterDiscount = Round2(row.MonthlyRent - discount);
            row.Tax = Round2(row.ServiceCharge * taxPercent / 100m);
            row.Total = Round2(row.RentAfterDiscount + row.Tax + (row.SecurityDeposit ?? 0m));
        }

        public static BookingScheduleTotals Totals(IEnumerable<BookingScheduleRow> rows)
        {
            var t = new BookingScheduleTotals();
            foreach (var r in rows)
            {
                t.MonthlyRent += r.MonthlyRent;
                t.DiscountAmount += r.DiscountAmount;
                t.RentAfterDiscount += r.RentAfterDiscount;
                t.SecurityDeposit += r.SecurityDeposit ?? 0m;
                t.Tax += r.Tax;
                t.Total += r.Total;
            }
            t.MonthlyRent = Round2(t.MonthlyRent); t.DiscountAmount = Round2(t.DiscountAmount);
            t.RentAfterDiscount = Round2(t.RentAfterDiscount); t.SecurityDeposit = Round2(t.SecurityDeposit);
            t.Tax = Round2(t.Tax); t.Total = Round2(t.Total);
            return t;
        }

        public static string Label(DateTime start, DateTime end)
        {
            var c = CultureInfo.InvariantCulture;
            bool wholeMonth = start.Day == 1 && start.Year == end.Year && start.Month == end.Month && end.Day == DateTime.DaysInMonth(end.Year, end.Month);
            if (wholeMonth) return start.ToString("MMM yyyy", c);
            return start.Year == end.Year
                ? $"{start.ToString("d MMM", c)} - {end.ToString("d MMM yyyy", c)}"
                : $"{start.ToString("d MMM yyyy", c)} - {end.ToString("d MMM yyyy", c)}";
        }

        // ---- validation / applying a client's rows -----------------------------------------------------------------------------

        /// <summary>
        /// Applies the client's rows (discount type / value, deposit) to the booking's current table and recomputes every un-invoiced
        /// row. Returns an error text (400) when the rows do not fit: wrong count / numbers, a bad discount, or a change to an invoiced row.
        /// </summary>
        public static (List<BookingScheduleRow>? Rows, string? Error) Apply(IReadOnlyList<BookingScheduleRow> current,
            IReadOnlyList<BookingScheduleInputRow>? input, decimal taxPercent)
        {
            if (input == null || input.Count == 0) return (null, "Send the rows of the rent schedule.");
            if (input.Count != current.Count)
                return (null, $"The rent schedule has {current.Count} months; {input.Count} rows were sent.");
            var byNo = new Dictionary<int, BookingScheduleInputRow>();
            foreach (var r in input)
            {
                if (r == null) return (null, "A row is empty.");
                if (!byNo.TryAdd(r.RowNo, r)) return (null, $"Row {r.RowNo} was sent twice.");
            }
            var result = new List<BookingScheduleRow>();
            foreach (var cur in current.OrderBy(c => c.RowNo))
            {
                if (!byNo.TryGetValue(cur.RowNo, out var inp))
                    return (null, $"Row {cur.RowNo} is missing.");
                var next = Clone(cur);
                var type = NormalizeDiscountType(inp.DiscountType);
                if (type == null) return (null, $"Row {cur.RowNo}: the discount type must be Percentage or Amount.");
                if (inp.DiscountValue < 0) return (null, $"Row {cur.RowNo}: the discount cannot be negative.");
                if (type == Percentage && inp.DiscountValue > 100) return (null, $"Row {cur.RowNo}: a percentage discount is between 0 and 100.");
                if (inp.SecurityDeposit is < 0) return (null, $"Row {cur.RowNo}: the security deposit cannot be negative.");
                decimal? dep = inp.SecurityDeposit is > 0 ? Round2(inp.SecurityDeposit.Value) : null;

                if (cur.Locked)
                {
                    bool same = type == NormalizeDiscountType(cur.DiscountType)
                        && Math.Abs(inp.DiscountValue - cur.DiscountValue) < 0.005m
                        && Math.Abs((dep ?? 0m) - (cur.SecurityDeposit ?? 0m)) < 0.005m;
                    if (!same) return (null, $"{cur.Label} is already invoiced ({cur.InvoiceNumber}) and cannot be changed.");
                }
                else
                {
                    next.DiscountType = type;
                    next.DiscountValue = Round2(inp.DiscountValue);
                    next.SecurityDeposit = dep;
                    Recompute(next, taxPercent);
                }
                result.Add(next);
            }
            return (result, null);
        }

        public static BookingScheduleRow Clone(BookingScheduleRow r) => new()
        {
            RowNo = r.RowNo, DiscountType = r.DiscountType, DiscountValue = r.DiscountValue, SecurityDeposit = r.SecurityDeposit,
            Label = r.Label, PeriodStart = r.PeriodStart, PeriodEnd = r.PeriodEnd, MonthFraction = r.MonthFraction, IsShort = r.IsShort,
            MonthlyRent = r.MonthlyRent, ServiceCharge = r.ServiceCharge, RoomRent = r.RoomRent, DiscountAmount = r.DiscountAmount,
            RentAfterDiscount = r.RentAfterDiscount, Tax = r.Tax, Total = r.Total, InvoiceId = r.InvoiceId, InvoiceNumber = r.InvoiceNumber, Locked = r.Locked
        };

        // ---- the first invoice window / grouping -------------------------------------------------------------------------------

        /// <summary>
        /// The period of the booking's first invoice, as InvoiceCalculationEngine.CalculateInvoice sets it for a monthly space: start on the
        /// 1st = <paramref name="billingPeriodMonths"/> months from the start; start on a later day = from the day after, start before the
        /// 15th = the short month + (months - 1) calendar months, from the 15th = the short month + months calendar months.
        /// </summary>
        public static (DateTime Start, DateTime End) FirstInvoicePeriod(DateTime startOn, int billingPeriodMonths)
        {
            int bpm = billingPeriodMonths > 0 ? billingPeriodMonths : 3;
            var s = startOn.Date;
            if (s.Day > 1)
            {
                var first = new DateTime(s.Year, s.Month, 1);
                var end = s.Day < 15 ? first.AddMonths(bpm).AddDays(-1) : first.AddMonths(bpm + 1).AddDays(-1);
                return (s.AddDays(1), end);
            }
            return (s, s.AddMonths(bpm).AddDays(-1));
        }

        /// <summary>The un-invoiced rows (in row order) that lie inside the first-invoice period; empty when there are none.</summary>
        public static List<BookingScheduleRow> FirstInvoiceGroup(IReadOnlyList<BookingScheduleRow> rows, DateTime startOn, int billingPeriodMonths)
        {
            var (_, end) = FirstInvoicePeriod(startOn, billingPeriodMonths);
            var group = new List<BookingScheduleRow>();
            foreach (var r in rows.OrderBy(r => r.RowNo))
            {
                if (r.Locked) return new List<BookingScheduleRow>();   // something is invoiced already: this is not a first invoice
                if (r.PeriodEnd.Date > end.Date) break;
                group.Add(r);
            }
            return group;
        }

        /// <summary>The next row to bill: the first un-invoiced row. Null when every month is invoiced.</summary>
        public static BookingScheduleRow? NextUninvoiced(IReadOnlyList<BookingScheduleRow> rows) =>
            rows.OrderBy(r => r.RowNo).FirstOrDefault(r => !r.Locked);

        /// <summary>The invoice figures of one or more rows (an invoice is the sum of its rows).</summary>
        public static ScheduleInvoiceFigures Figures(IReadOnlyList<BookingScheduleRow> group)
        {
            if (group == null || group.Count == 0) throw new ArgumentException("No rows to invoice.", nameof(group));
            var ordered = group.OrderBy(r => r.RowNo).ToList();
            var f = new ScheduleInvoiceFigures
            {
                PeriodStart = ordered.Min(r => r.PeriodStart).Date,
                PeriodEnd = ordered.Max(r => r.PeriodEnd).Date,
                Rent = Round2(ordered.Sum(r => r.MonthlyRent)),
                Discount = Round2(ordered.Sum(r => r.DiscountAmount)),
                RentAfterDiscount = Round2(ordered.Sum(r => r.RentAfterDiscount)),
                ServiceCharge = Round2(ordered.Sum(r => r.ServiceCharge)),
                Tax = Round2(ordered.Sum(r => r.Tax)),
                Deposit = Round2(ordered.Sum(r => r.SecurityDeposit ?? 0m)),
                GrandTotal = Round2(ordered.Sum(r => r.Total)),
                MonthsExact = ordered.Sum(r => r.MonthFraction),
                RowNos = ordered.Select(r => r.RowNo).ToList()
            };
            f.Months = Math.Max(1, (int)Math.Round(f.MonthsExact, MidpointRounding.AwayFromZero));
            f.RoomRentExclTax = Math.Max(0m, f.RentAfterDiscount - f.ServiceCharge);
            return f;
        }

        // ---- the projection of an old booking ----------------------------------------------------------------------------------

        /// <summary>
        /// The table of a booking that has none yet: one locked row per invoice already made, then one row per remaining billing month,
        /// priced as today's invoices price them (see <see cref="BuildProjectedRow"/>). Months are the Billing Automation job's:
        /// BillingPeriods.PeriodEnd(contractStart, start, 1), the last one cut at the contract end (fraction BillingPeriods.MonthsBetween).
        /// Before the first invoice the first-invoice period is added as calendar-month rows (short first month first).
        /// </summary>
        public static List<BookingScheduleRow> Project(BookingProjectionInput p)
        {
            var rows = new List<BookingScheduleRow>();
            int no = 0;
            DateTime? lastEnd = null;
            foreach (var inv in p.Invoiced.OrderBy(i => i.Start).ThenBy(i => i.InvoiceId))
            {
                no++;
                var row = new BookingScheduleRow
                {
                    RowNo = no, PeriodStart = inv.Start.Date, PeriodEnd = inv.End.Date,
                    MonthFraction = inv.Months > 0 ? inv.Months : BillingPeriods.MonthsBetween(inv.Start, inv.End),
                    MonthlyRent = Round2(inv.SubTotal), ServiceCharge = Round2(inv.ServiceCharges),
                    DiscountType = Amount, DiscountValue = Round2(inv.DiscountTotal), DiscountAmount = Round2(inv.DiscountTotal),
                    SecurityDeposit = inv.SecurityDeposit > 0 ? Round2(inv.SecurityDeposit) : null,
                    Tax = Round2(inv.TaxTotal), Total = Round2(inv.GrandTotal),
                    InvoiceId = inv.InvoiceId, InvoiceNumber = inv.InvoiceNumber, Locked = true
                };
                row.RoomRent = Math.Max(0m, row.MonthlyRent - row.ServiceCharge);
                row.RentAfterDiscount = Round2(row.MonthlyRent - row.DiscountAmount);
                row.IsShort = row.MonthFraction < 1m;
                row.Label = Label(row.PeriodStart, row.PeriodEnd);
                rows.Add(row);
                if (lastEnd == null || row.PeriodEnd > lastEnd) lastEnd = row.PeriodEnd;
            }

            var endOn = p.ContractEnd.Date;
            var contractStart = p.ContractStart.Date;
            DateTime start;
            if (lastEnd.HasValue)
            {
                start = lastEnd.Value.AddDays(1);
            }
            else
            {
                // Nothing invoiced yet: the first invoice's period, month by month (calendar months, the short first month first).
                var (fs, fe) = FirstInvoicePeriod(contractStart, p.BillingPeriodMonths);
                var cursor = fs;
                bool firstRow = true;
                if (contractStart.Day > 1)
                {
                    decimal shortFraction = (decimal)(DaysPerMonth - Math.Min(contractStart.Day, DaysPerMonth)) / DaysPerMonth;
                    var monthEnd = new DateTime(contractStart.Year, contractStart.Month, 1).AddMonths(1).AddDays(-1);
                    if (shortFraction > 0m && fs < endOn)
                    {
                        var pe = monthEnd > endOn ? endOn : monthEnd;
                        if (pe > fe) pe = fe;
                        no++;
                        var row = BuildProjectedRow(no, fs, pe, shortFraction, p);
                        if (p.InitialDeposit > 0) { row.SecurityDeposit = Round2(p.InitialDeposit); Recompute(row, p.TaxPercent); }
                        rows.Add(row);
                        firstRow = false;
                    }
                    cursor = monthEnd.AddDays(1);
                }
                while (cursor <= fe && cursor < endOn)
                {
                    var pe = new DateTime(cursor.Year, cursor.Month, 1).AddMonths(1).AddDays(-1);
                    decimal fraction = 1m;
                    if (pe > endOn) { pe = endOn; var partial = BillingPeriods.MonthsBetween(cursor, pe); if (partial > 0 && partial < 1m) fraction = partial; }
                    if (pe > fe) pe = fe;
                    no++;
                    var row = BuildProjectedRow(no, cursor, pe, fraction, p);
                    if (firstRow && p.InitialDeposit > 0) { row.SecurityDeposit = Round2(p.InitialDeposit); Recompute(row, p.TaxPercent); }
                    rows.Add(row);
                    firstRow = false;
                    cursor = pe.AddDays(1);
                }
                start = cursor;
            }

            // The remaining months, exactly as the Billing Automation job bills them: one month at a time, anchored to the contract start.
            while (start < endOn)
            {
                var raw = BillingPeriods.PeriodEnd(contractStart, start, 1);
                var periodEnd = raw > endOn ? endOn : raw;
                decimal fraction = 1m;
                if (raw > endOn)
                {
                    var partial = BillingPeriods.MonthsBetween(start, periodEnd);
                    if (partial > 0 && partial < 1m) fraction = partial;
                }
                no++;
                rows.Add(BuildProjectedRow(no, start, periodEnd, fraction, p));
                start = periodEnd.AddDays(1);
            }
            return rows;
        }

        /// <summary>
        /// One projected month priced like today's recurring invoice (GenerateNextInvoiceCoreAsync / WN_Invoice_CreateRecurring):
        /// gross = round2(monthly x fraction); a percentage discount = round2(gross x % / 100) (of the whole rent, service charge included);
        /// a flat amount discount is per month = amount x fraction, never above the gross; service charge = rate x seats x fraction.
        /// The discount is stored as an Amount, so the table's "percentage of room rent" rule cannot change what is billed.
        /// </summary>
        public static BookingScheduleRow BuildProjectedRow(int rowNo, DateTime start, DateTime end, decimal fraction, BookingProjectionInput p)
        {
            var (rent, service) = BaseAmounts(p.MonthlyRent, p.SupportRate, p.Seats, fraction);
            decimal discount = 0m;
            if (p.DiscountPercentage > 0) discount = Round2(rent * (p.DiscountPercentage / 100m));
            else if (p.DiscountAmount > 0) discount = Round2(Math.Min(p.DiscountAmount * fraction, rent));
            decimal roomRent = Math.Max(0m, rent - service);
            if (discount > roomRent) discount = roomRent;   // the service charge is never discounted in the table
            var row = new BookingScheduleRow
            {
                RowNo = rowNo, PeriodStart = start.Date, PeriodEnd = end.Date, MonthFraction = fraction, IsShort = fraction < 1m,
                MonthlyRent = rent, ServiceCharge = service, DiscountType = Amount, DiscountValue = discount,
                Label = Label(start, end)
            };
            Recompute(row, p.TaxPercent);
            return row;
        }
    }
}
