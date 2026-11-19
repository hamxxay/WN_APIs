using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WorkNest.Application.DTOs.Quotation;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Rent schedule (month-by-month table on a quotation / booking, script CQ_01): the periods, the maths and the validation. Pure rules, no I/O.
    ///
    /// Periods follow the way invoices bill (30-day months, InvoiceCalculationEngine): a start on the 1st gives calendar months; a start on day d &gt; 1
    /// gives a first short row (d+1 .. end of month, fraction (30 - min(d,30)) / 30); a contract end that is not a month end gives a last short row
    /// (1st .. end day, fraction min(endDay,30) / 30).
    ///
    /// Maths per row (the invoice maths: the monthly rent INCLUDES the service charge; the manual discount is taken off the gross rent; tax is on
    /// the service charge only): monthlyRent = round2(perSeat x seats x fraction); serviceCharge = round2(supportRate x seats x fraction);
    /// roomRent = max(0, monthlyRent - serviceCharge); discountAmount = Percentage: round2(monthlyRent x v / 100) | Amount: min(v, monthlyRent);
    /// rentAfterDiscount = monthlyRent - discountAmount; tax = round2(serviceCharge x taxPercent / 100); total = rentAfterDiscount + tax + deposit.
    /// </summary>
    public static class RentSchedule
    {
        public const int DaysPerMonth = 30;
        public const decimal DefaultSupportRate = 2000.00m;
        public const string NotSetUpMessage = "Rent schedule is not set up on this server yet.";
        public const string LockedMessage = "The rent schedule cannot be changed once the quotation has been sent. Make a new version.";
        public const string ActionScheduleEdited = "schedule_edited";
        public const string Percentage = "Percentage";
        public const string Amount = "Amount";

        /// <summary>Statuses after which the table is locked (spec decision 5).</summary>
        private static readonly string[] LockedStatuses = { "Sent", "Approved", "Accepted", "AgreementSent", "Signed", "Converted" };

        public static bool IsLockedStatus(string? status) =>
            !string.IsNullOrWhiteSpace(status) && LockedStatuses.Any(s => string.Equals(s, status.Trim(), StringComparison.OrdinalIgnoreCase));

        public static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        /// <summary>One calendar slice of the contract. <see cref="Fraction"/> is exact (n / 30).</summary>
        public sealed record Period(int RowNo, DateTime Start, DateTime End, decimal Fraction, bool IsShort, string Label);

        /// <summary>The per-seat price, seats, per-seat service charge and tax percent a table is worked out with.</summary>
        public sealed record Inputs(decimal PerSeatPrice, int Seats, decimal SupportRate, decimal TaxPercent);

        private static DateTime EndOfMonth(DateTime d) => new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
        private static DateTime FirstOfNextMonth(DateTime d) => new DateTime(d.Year, d.Month, 1).AddMonths(1);

        private static string FullLabel(DateTime start) => start.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        private static string ShortLabel(DateTime start, DateTime end) =>
            $"{start.Day}-{end.Day} {end.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";

        /// <summary>The rows of a contract from its start to its end date (both included). Throws <see cref="ArgumentException"/> when the dates give no month.</summary>
        public static IReadOnlyList<Period> BuildPeriods(DateTime startDate, DateTime endDate)
        {
            var s = startDate.Date;
            var e = endDate.Date;
            if (e <= s) throw new ArgumentException("The end date must be after the start date.");

            var list = new List<Period>();
            void Add(DateTime from, DateTime to, decimal fraction, bool isShort) =>
                list.Add(new Period(list.Count + 1, from, to, fraction, isShort, isShort ? ShortLabel(from, to) : FullLabel(from)));

            DateTime monthStart;
            if (s.Day == 1)
            {
                monthStart = s;
            }
            else
            {
                var eom = EndOfMonth(s);
                int d = Math.Min(s.Day, DaysPerMonth);
                if (e <= eom)
                {
                    // the whole contract is inside the start month: (d+1 .. end day)
                    int n = Math.Min(e.Day, DaysPerMonth) - d;
                    if (n <= 0) throw new ArgumentException("The contract is too short to make a rent schedule.");
                    Add(s.AddDays(1), e, (decimal)n / DaysPerMonth, true);
                    return list;
                }
                int stubDays = DaysPerMonth - d;
                if (s < eom && stubDays > 0)
                    Add(s.AddDays(1), eom, (decimal)stubDays / DaysPerMonth, true);
                monthStart = FirstOfNextMonth(s);
            }

            while (monthStart <= e)
            {
                var eom = EndOfMonth(monthStart);
                if (e >= eom) Add(monthStart, eom, 1m, false);
                else Add(monthStart, e, (decimal)Math.Min(e.Day, DaysPerMonth) / DaysPerMonth, true);
                monthStart = FirstOfNextMonth(monthStart);
            }
            if (list.Count == 0) throw new ArgumentException("The contract is too short to make a rent schedule.");
            return list;
        }

        /// <summary>A stored (6 decimals) fraction back to the exact n / 30 (fractions are always whole 30ths).</summary>
        public static decimal ExactFraction(decimal stored) =>
            stored >= 1m ? 1m : stored <= 0m ? 0m : Math.Round(stored * DaysPerMonth, 0, MidpointRounding.AwayFromZero) / DaysPerMonth;

        public static string NormalizeType(string? type)
        {
            var t = type?.Trim();
            if (string.Equals(t, "Percentage", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "Percent", StringComparison.OrdinalIgnoreCase)) return Percentage;
            if (string.Equals(t, "Amount", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "Fixed", StringComparison.OrdinalIgnoreCase)) return Amount;
            throw new ArgumentException("Discount type must be Percentage or Amount.");
        }

        /// <summary>The table the server builds from the form: the form's discount on every row (an Amount scaled by the row's fraction), the whole deposit on row 1.</summary>
        public static List<ScheduleInputRow> DefaultRows(IReadOnlyList<Period> periods, string? discountType, decimal discountValue, decimal totalDeposit)
        {
            string type = string.IsNullOrWhiteSpace(discountType) ? Percentage : NormalizeType(discountType);
            var rows = new List<ScheduleInputRow>();
            foreach (var p in periods)
                rows.Add(new ScheduleInputRow
                {
                    RowNo = p.RowNo,
                    DiscountType = type,
                    DiscountValue = discountValue <= 0m ? 0m : (type == Amount ? R2(discountValue * p.Fraction) : discountValue),
                    SecurityDeposit = p.RowNo == 1 && totalDeposit > 0m ? R2(totalDeposit) : null
                });
            return rows;
        }

        /// <summary>Checks the user's rows against the periods (400 messages in plain English) and returns them ordered by row number, deposits rounded.</summary>
        public static List<ScheduleInputRow> ValidateRows(IReadOnlyList<Period> periods, IReadOnlyList<ScheduleInputRow>? rows, decimal? expectedTotalDeposit = null)
        {
            if (rows == null || rows.Count == 0) throw new ArgumentException("The rent schedule has no rows.");
            if (rows.Count != periods.Count)
                throw new ArgumentException($"The rent schedule has {rows.Count} row(s) but the contract has {periods.Count} month(s). Rebuild the table.");
            var ordered = rows.OrderBy(r => r.RowNo).ToList();
            for (int i = 0; i < ordered.Count; i++)
                if (ordered[i].RowNo != i + 1) throw new ArgumentException("The rent schedule rows must be numbered 1 to " + periods.Count + ".");

            var result = new List<ScheduleInputRow>();
            foreach (var r in ordered)
            {
                string type = NormalizeType(r.DiscountType);
                if (r.DiscountValue < 0m) throw new ArgumentException($"Month {r.RowNo}: the discount cannot be negative.");
                if (type == Percentage && r.DiscountValue > 100m) throw new ArgumentException($"Month {r.RowNo}: a percentage discount can be at most 100.");
                if (r.DiscountValue > 1_000_000_000m) throw new ArgumentException($"Month {r.RowNo}: the discount is too large.");
                if (r.SecurityDeposit is < 0m) throw new ArgumentException($"Month {r.RowNo}: the security deposit cannot be negative.");
                if (r.SecurityDeposit > 1_000_000_000m) throw new ArgumentException($"Month {r.RowNo}: the security deposit is too large.");
                result.Add(new ScheduleInputRow
                {
                    RowNo = r.RowNo,
                    DiscountType = type,
                    DiscountValue = type == Percentage ? Math.Round(r.DiscountValue, 4, MidpointRounding.AwayFromZero) : R2(r.DiscountValue),
                    SecurityDeposit = r.SecurityDeposit is > 0m ? R2(r.SecurityDeposit.Value) : null
                });
            }
            if (expectedTotalDeposit.HasValue)
            {
                decimal sum = result.Sum(r => r.SecurityDeposit ?? 0m);
                if (Math.Abs(sum - R2(expectedTotalDeposit.Value)) > 0.01m)
                    throw new ArgumentException($"The security deposits in the schedule add up to PKR {sum.ToString("N0", CultureInfo.InvariantCulture)} but the deposit is PKR {R2(expectedTotalDeposit.Value).ToString("N0", CultureInfo.InvariantCulture)}.");
            }
            return result;
        }

        /// <summary>Works out every column for validated rows (see the class summary for the formulas).</summary>
        public static RentScheduleDto Compute(IReadOnlyList<Period> periods, Inputs inputs, IReadOnlyList<ScheduleInputRow> rows, decimal defaultSecurityDeposit)
        {
            var dto = new RentScheduleDto
            {
                TaxPercent = inputs.TaxPercent, SupportRate = inputs.SupportRate, Seats = inputs.Seats,
                PerSeatPrice = inputs.PerSeatPrice, DefaultSecurityDeposit = R2(defaultSecurityDeposit)
            };
            foreach (var p in periods)
            {
                var r = rows.First(x => x.RowNo == p.RowNo);
                dto.Rows.Add(ComputeRow(p.RowNo, p.Start, p.End, p.Fraction, p.IsShort, p.Label, inputs, r.DiscountType, r.DiscountValue, r.SecurityDeposit));
            }
            dto.Totals = TotalsOf(dto.Rows);
            return dto;
        }

        private static ScheduleRowDto ComputeRow(int rowNo, DateTime start, DateTime end, decimal fraction, bool isShort, string label,
            Inputs inputs, string? discountType, decimal discountValue, decimal? deposit)
        {
            decimal monthly = R2(inputs.PerSeatPrice * inputs.Seats * fraction);
            decimal service = R2(inputs.SupportRate * inputs.Seats * fraction);
            string type = NormalizeType(discountType);
            decimal discount = type == Amount ? Math.Min(Math.Max(0m, discountValue), monthly) : R2(monthly * Math.Clamp(discountValue, 0m, 100m) / 100m);
            decimal after = monthly - discount;
            decimal tax = R2(service * inputs.TaxPercent / 100m);
            return new ScheduleRowDto
            {
                RowNo = rowNo, Label = label, PeriodStart = DateOnly.FromDateTime(start), PeriodEnd = DateOnly.FromDateTime(end),
                MonthFraction = fraction, IsShort = isShort,
                MonthlyRent = monthly, ServiceCharge = service, RoomRent = Math.Max(0m, monthly - service),
                DiscountType = type, DiscountValue = discountValue, DiscountAmount = discount,
                RentAfterDiscount = after, Tax = tax, SecurityDeposit = deposit is > 0m ? deposit : null,
                Total = after + tax + (deposit ?? 0m)
            };
        }

        public static ScheduleTotalsDto TotalsOf(IEnumerable<ScheduleRowDto> rows)
        {
            var list = rows.ToList();
            return new ScheduleTotalsDto
            {
                MonthlyRent = list.Sum(r => r.MonthlyRent),
                DiscountAmount = list.Sum(r => r.DiscountAmount),
                RentAfterDiscount = list.Sum(r => r.RentAfterDiscount),
                SecurityDeposit = list.Sum(r => r.SecurityDeposit ?? 0m),
                Tax = list.Sum(r => r.Tax),
                Total = list.Sum(r => r.Total)
            };
        }

        // ---- storage mapping -------------------------------------------------------------------------------------------------

        public static List<StoredScheduleRow> ToStored(RentScheduleDto schedule) =>
            schedule.Rows.Select(r => new StoredScheduleRow
            {
                RowNo = r.RowNo, PeriodStart = r.PeriodStart.ToDateTime(TimeOnly.MinValue), PeriodEnd = r.PeriodEnd.ToDateTime(TimeOnly.MinValue),
                MonthFraction = Math.Round(r.MonthFraction, 6, MidpointRounding.AwayFromZero),
                MonthlyRent = r.MonthlyRent, ServiceCharge = r.ServiceCharge, RoomRent = r.RoomRent,
                DiscountType = r.DiscountType, DiscountValue = r.DiscountValue, DiscountAmount = r.DiscountAmount,
                RentAfterDiscount = r.RentAfterDiscount, Tax = r.Tax, SecurityDeposit = r.SecurityDeposit, Total = r.Total
            }).ToList();

        /// <summary>The stored rows as quoted (amounts are what was saved, not worked out again); the header fields come from the quotation.</summary>
        public static RentScheduleDto FromStored(IReadOnlyList<StoredScheduleRow> rows, Inputs inputs, decimal defaultSecurityDeposit)
        {
            var dto = new RentScheduleDto
            {
                TaxPercent = inputs.TaxPercent, SupportRate = inputs.SupportRate, Seats = inputs.Seats,
                PerSeatPrice = inputs.PerSeatPrice, DefaultSecurityDeposit = R2(defaultSecurityDeposit)
            };
            foreach (var r in rows.OrderBy(x => x.RowNo))
            {
                decimal fraction = ExactFraction(r.MonthFraction);
                bool isShort = fraction < 1m;
                dto.Rows.Add(new ScheduleRowDto
                {
                    RowNo = r.RowNo, Label = isShort ? ShortLabel(r.PeriodStart, r.PeriodEnd) : FullLabel(r.PeriodStart),
                    PeriodStart = DateOnly.FromDateTime(r.PeriodStart), PeriodEnd = DateOnly.FromDateTime(r.PeriodEnd),
                    MonthFraction = fraction, IsShort = isShort,
                    MonthlyRent = r.MonthlyRent, ServiceCharge = r.ServiceCharge, RoomRent = r.RoomRent,
                    DiscountType = r.DiscountType, DiscountValue = r.DiscountValue, DiscountAmount = r.DiscountAmount,
                    RentAfterDiscount = r.RentAfterDiscount, Tax = r.Tax, SecurityDeposit = r.SecurityDeposit, Total = r.Total
                });
            }
            dto.Totals = TotalsOf(dto.Rows);
            return dto;
        }

        /// <summary>The rows as the user inputs (what a client would post back).</summary>
        public static List<ScheduleInputRow> InputsOf(RentScheduleDto schedule) =>
            schedule.Rows.Select(r => new ScheduleInputRow { RowNo = r.RowNo, DiscountType = r.DiscountType, DiscountValue = r.DiscountValue, SecurityDeposit = r.SecurityDeposit }).ToList();

        /// <summary>The periods of a stored schedule (so a manager's edit is validated against the quoted months).</summary>
        public static List<Period> PeriodsOf(IReadOnlyList<StoredScheduleRow> rows) =>
            rows.OrderBy(r => r.RowNo).Select(r =>
            {
                decimal f = ExactFraction(r.MonthFraction);
                bool isShort = f < 1m;
                return new Period(r.RowNo, r.PeriodStart, r.PeriodEnd, f, isShort, isShort ? ShortLabel(r.PeriodStart, r.PeriodEnd) : FullLabel(r.PeriodStart));
            }).ToList();

        // ---- history text ----------------------------------------------------------------------------------------------------

        private static string Disc(string type, decimal v) =>
            type == Amount ? "PKR " + v.ToString("N0", CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture) + "%";
        private static string Dep(decimal? d) => d is > 0m ? "PKR " + d.Value.ToString("N0", CultureInfo.InvariantCulture) : "none";

        /// <summary>A short plain-English list of what a manager changed ("Month 3 discount 10% -> 15%"), at most <paramref name="maxLength"/> characters; empty = nothing changed.</summary>
        public static string DescribeChanges(IReadOnlyList<ScheduleInputRow> before, IReadOnlyList<ScheduleInputRow> after, int maxLength = 900)
        {
            var changes = new List<string>();
            foreach (var a in after.OrderBy(x => x.RowNo))
            {
                var b = before.FirstOrDefault(x => x.RowNo == a.RowNo);
                if (b == null) continue;
                string bt = NormalizeType(b.DiscountType), at = NormalizeType(a.DiscountType);
                if (bt != at || b.DiscountValue != a.DiscountValue)
                    changes.Add($"Month {a.RowNo} discount {Disc(bt, b.DiscountValue)} -> {Disc(at, a.DiscountValue)}");
                if ((b.SecurityDeposit ?? 0m) != (a.SecurityDeposit ?? 0m))
                    changes.Add($"Month {a.RowNo} deposit {Dep(b.SecurityDeposit)} -> {Dep(a.SecurityDeposit)}");
            }
            if (changes.Count == 0) return string.Empty;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < changes.Count; i++)
            {
                string piece = (i == 0 ? "" : "; ") + changes[i];
                if (sb.Length + piece.Length > maxLength - 30)
                {
                    sb.Append($"; and {changes.Count - i} more change(s)");
                    break;
                }
                sb.Append(piece);
            }
            return sb.ToString();
        }
    }
}
