using WorkNest.Application.DTOs.RentSchedule;
using WorkNest.Application.DTOs.Transfer;
using WorkNest.Application.Services.BookingSchedules;

namespace WorkNest.Application.Services.Transfers
{
    /// <summary>Everything the plan maths needs (resolved by the service from the old booking and the new space).</summary>
    public sealed class TransferPlanInput
    {
        public int BookingId { get; set; }
        public int NewSpaceId { get; set; }
        public string? NewSpaceName { get; set; }
        public DateTime EffectiveDate { get; set; }
        public DateTime ContractEnd { get; set; }
        /// <summary>The old booking's rent schedule (stored or projected; Locked = invoiced).</summary>
        public IReadOnlyList<BookingScheduleRow> OldRows { get; set; } = Array.Empty<BookingScheduleRow>();
        public int OldSeats { get; set; } = 1;
        public decimal OldPerSeatPrice { get; set; }
        public decimal OldDeposit { get; set; }
        public int NewSeats { get; set; } = 1;
        public decimal ListPerSeatPrice { get; set; }
        public int SecurityDepositMonths { get; set; }
        public decimal TaxPercent { get; set; } = 16m;
        public decimal SupportRate { get; set; } = 2000m;
        /// <summary>Offering type discount cap in percent (0 = unknown); a row above it only warns.</summary>
        public decimal DiscountCapPercent { get; set; }
    }

    /// <summary>The plan plus what executing it passes to WN_Booking_TransferSpace.</summary>
    public sealed class TransferPlanResult
    {
        public TransferPlanDto Plan { get; set; } = new();
        public List<BookingScheduleRow> Rows { get; set; } = new();
        public decimal SettleRent { get; set; }
        public decimal SettleServiceCharge { get; set; }
        public decimal SettleTax { get; set; }
        public decimal CreditAmount { get; set; }
        public decimal RefundAmount { get; set; }
        public DateTime? NextBillingDate { get; set; }
        public bool HasErrors => Plan.Errors.Count > 0;
    }

    /// <summary>Space transfer plan maths (pure, static). See the redesign contract, "Plan maths".</summary>
    public static class TransferPlanCalculator
    {
        public const string Keep = "keep", List = "list", Custom = "custom", Recalc = "recalc";
        public const string AtOnce = "atonce", Monthly = "monthly", CreditMode = "credit", RefundMode = "refund";

        private static decimal R2(decimal v) => BookingScheduleMath.Round2(v);

        public static TransferPlanResult Calculate(TransferPlanInput input, TransferPlanRequest req)
        {
            var res = new TransferPlanResult();
            var plan = res.Plan;
            plan.BookingId = input.BookingId; plan.NewSpaceId = input.NewSpaceId; plan.NewSpaceName = input.NewSpaceName;
            plan.EffectiveDate = input.EffectiveDate.Date; plan.ContractEnd = input.ContractEnd.Date;
            plan.OldSeats = input.OldSeats; plan.NewSeats = input.NewSeats;
            plan.OldPerSeatPrice = input.OldPerSeatPrice; plan.ListPerSeatPrice = input.ListPerSeatPrice;
            plan.OldMonthlyRent = R2(input.OldPerSeatPrice * Math.Max(1, input.OldSeats));
            plan.OldDeposit = R2(input.OldDeposit); plan.TaxPercent = input.TaxPercent; plan.TransferFee = R2(req.TransferFee);
            var errors = plan.Errors; var warnings = plan.Warnings;

            if (req.TransferFee < 0) errors.Add("The transfer fee cannot be negative.");
            if (input.NewSeats < 1) errors.Add("The new space has no seats.");

            // price
            plan.PriceMode = (req.PriceMode ?? Keep).Trim().ToLowerInvariant();
            decimal perSeat = 0m;
            switch (plan.PriceMode)
            {
                case Keep: perSeat = input.OldPerSeatPrice; break;
                case List:
                    perSeat = input.ListPerSeatPrice;
                    if (perSeat <= 0) errors.Add("The new space has no list price per seat."); break;
                case Custom:
                    if (req.PerSeatPrice is not > 0) errors.Add("Enter the price per seat."); else perSeat = R2(req.PerSeatPrice.Value);
                    break;
                default: errors.Add("The price mode must be keep, list or custom."); break;
            }
            plan.PerSeatPrice = perSeat;
            decimal newMonthly = R2(perSeat * Math.Max(1, input.NewSeats));
            plan.NewMonthlyRent = newMonthly;

            // deposit
            plan.DepositMode = (req.DepositMode ?? Keep).Trim().ToLowerInvariant();
            decimal newDeposit = plan.OldDeposit;
            plan.DepositMonths = input.SecurityDepositMonths;
            switch (plan.DepositMode)
            {
                case Keep: break;
                case Recalc:
                    {
                        int months = req.DepositMonths ?? input.SecurityDepositMonths;
                        if (req.DepositMonths is < 1 or > 12) errors.Add("Deposit months must be between 1 and 12.");
                        else if (months <= 0) errors.Add("Choose how many months of rent the security deposit is.");
                        plan.DepositMonths = Math.Max(0, months);
                        newDeposit = R2(Math.Max(0, months) * newMonthly); break;
                    }
                case Custom:
                    if (req.NewDeposit is null or < 0) errors.Add("Enter the new security deposit."); else newDeposit = R2(req.NewDeposit.Value);
                    break;
                default: errors.Add("The deposit mode must be keep, recalc or custom."); break;
            }
            plan.NewDeposit = newDeposit;
            plan.DepositTopUp = Math.Max(0m, newDeposit - plan.OldDeposit);
            plan.DepositDecrease = Math.Max(0m, plan.OldDeposit - newDeposit);
            if (plan.DepositDecrease > 0)
            {
                var m = (req.DepositDecreaseMode ?? "").Trim().ToLowerInvariant();
                if (m == "") m = CreditMode;   // not chosen = credit
                if (m != CreditMode && m != RefundMode) errors.Add("The deposit decrease mode must be credit or refund.");
                else plan.DepositDecreaseMode = m;
            }

            plan.PrepaidMode = string.IsNullOrWhiteSpace(req.PrepaidMode) ? AtOnce : req.PrepaidMode.Trim().ToLowerInvariant();
            if (plan.PrepaidMode != AtOnce && plan.PrepaidMode != Monthly) errors.Add("The prepaid mode must be atonce or monthly.");

            // rows
            var eff = input.EffectiveDate.Date;
            var rows = new List<BookingScheduleRow>();
            int no = 0;
            foreach (var old in input.OldRows.OrderBy(r => r.RowNo))
            {
                if (old.PeriodEnd.Date < eff || old.PeriodStart.Date > input.ContractEnd.Date) continue;
                var start = old.PeriodStart.Date > eff ? old.PeriodStart.Date : eff;
                var end = old.PeriodEnd.Date;
                int total = Math.Max(1, (old.PeriodEnd.Date - old.PeriodStart.Date).Days + 1);
                int used = (end - start).Days + 1;
                decimal p = Math.Min(1m, (decimal)used / total);
                decimal fraction = Math.Round(old.MonthFraction * p, 6);
                var (rent, sc) = BookingScheduleMath.BaseAmounts(newMonthly, input.SupportRate, input.NewSeats, fraction);
                var type = BookingScheduleMath.NormalizeDiscountType(old.DiscountType) ?? BookingScheduleMath.Amount;
                var row = new BookingScheduleRow
                {
                    RowNo = ++no, PeriodStart = start, PeriodEnd = end, MonthFraction = fraction, IsShort = fraction < 1m,
                    MonthlyRent = rent, ServiceCharge = sc, DiscountType = type,
                    DiscountValue = type == BookingScheduleMath.Percentage ? old.DiscountValue : R2(old.DiscountValue * p),
                    Label = BookingScheduleMath.Label(start, end)
                };
                BookingScheduleMath.Recompute(row, input.TaxPercent);
                if (old.Locked)
                {
                    row.PaidRent = R2(old.RentAfterDiscount * p);
                    row.PaidServiceCharge = R2(old.ServiceCharge * p);
                    row.PaidTax = R2(old.Tax * p);
                    row.InvoiceNumber = old.InvoiceNumber;   // the old invoice (shown only)
                }
                rows.Add(row);
            }
            if (rows.Count == 0) errors.Add("There are no remaining contract months from the effective date.");

            // edits (discount)
            var edits = new Dictionary<int, TransferPlanRowEdit>();
            if (req.Rows != null)
                foreach (var e in req.Rows)
                {
                    if (e == null) continue;
                    if (!edits.TryAdd(e.RowNo, e)) { errors.Add($"Row {e.RowNo} was sent twice."); continue; }
                    if (rows.All(r => r.RowNo != e.RowNo)) errors.Add($"Row {e.RowNo} does not exist.");
                }
            foreach (var row in rows)
            {
                if (!edits.TryGetValue(row.RowNo, out var e)) continue;
                var t = BookingScheduleMath.NormalizeDiscountType(e.DiscountType);
                if (t == null) { errors.Add($"Row {row.RowNo}: the discount type must be Percentage or Amount."); continue; }
                if (e.DiscountValue < 0) { errors.Add($"Row {row.RowNo}: the discount cannot be negative."); continue; }
                if (t == BookingScheduleMath.Percentage && e.DiscountValue > 100) { errors.Add($"Row {row.RowNo}: a percentage discount is between 0 and 100."); continue; }
                row.DiscountType = t; row.DiscountValue = R2(e.DiscountValue);
                BookingScheduleMath.Recompute(row, input.TaxPercent);
            }

            // settlement of the prepaid rows
            decimal sRent = 0, sSc = 0, sTax = 0, credit = 0;
            foreach (var row in rows)
            {
                bool prepaid = row.PaidRent != null || row.PaidTax != null;
                if (!prepaid) continue;
                decimal diff = BookingScheduleMath.Difference(row);
                if (diff <= 0m) { row.TransferSettled = true; credit += -diff; continue; }
                if (plan.PrepaidMode == AtOnce)
                {
                    var b = BookingScheduleMath.Billed(row);
                    sRent += b.RentAfterDiscount; sSc += b.ServiceCharge; sTax += b.Tax;
                    row.TransferSettled = true;
                }
            }
            res.SettleRent = R2(sRent); res.SettleServiceCharge = R2(sSc); res.SettleTax = R2(sTax);

            // deposit top-up on the rows
            var billable = rows.Where(r => !r.TransferSettled).OrderBy(r => r.RowNo).ToList();
            bool split = edits.Values.Any(e => e.SecurityDeposit != null);
            if (split)
            {
                decimal sum = 0m;
                foreach (var row in rows)
                {
                    decimal d = edits.TryGetValue(row.RowNo, out var e) ? R2(e.SecurityDeposit ?? 0m) : 0m;
                    if (d < 0) { errors.Add($"Row {row.RowNo}: the security deposit cannot be negative."); continue; }
                    if (d > 0 && row.TransferSettled) { errors.Add($"Row {row.RowNo} is settled at the transfer: put the deposit on a month that is still billed."); continue; }
                    row.SecurityDeposit = d > 0 ? d : null; sum += d;
                }
                if (Math.Abs(sum - plan.DepositTopUp) > 0.005m)
                    errors.Add($"The security deposit split ({sum:N2}) must add up to the top-up ({plan.DepositTopUp:N2}).");
            }
            else if (plan.DepositTopUp > 0)
            {
                if (billable.Count == 0) errors.Add("Every remaining month is settled at the transfer: there is no month to bill the security deposit top-up in.");
                else billable[0].SecurityDeposit = plan.DepositTopUp;
            }
            foreach (var row in rows) BookingScheduleMath.Recompute(row, input.TaxPercent);

            // discount cap (warning only)
            if (input.DiscountCapPercent > 0)
                foreach (var row in rows)
                    if (row.MonthlyRent > 0 && row.DiscountAmount / row.MonthlyRent * 100m > input.DiscountCapPercent + 0.005m)
                        warnings.Add($"{row.Label}: the discount is above the {input.DiscountCapPercent:0.##}% cap of the offering type.");

            // credit, refund, settlement
            decimal refund = 0m;
            if (plan.DepositDecrease > 0)
            {
                if (plan.DepositDecreaseMode == CreditMode) credit += plan.DepositDecrease;
                else if (plan.DepositDecreaseMode == RefundMode) refund = plan.DepositDecrease;
            }
            res.CreditAmount = R2(credit); res.RefundAmount = R2(refund);
            plan.Settlement = new TransferPlanSettlementDto
            {
                InvoiceRent = res.SettleRent, InvoiceServiceCharge = res.SettleServiceCharge, InvoiceTax = res.SettleTax,
                InvoiceTotal = R2(res.SettleRent + res.SettleTax + plan.TransferFee), Credit = res.CreditAmount, Refund = res.RefundAmount
            };

            res.NextBillingDate = rows.Count == 0 ? null
                : (billable.Count > 0 ? billable.Where(r => !r.TransferSettled).OrderBy(r => r.RowNo).First().PeriodStart
                    : rows.Max(r => r.PeriodEnd).AddDays(1));
            var locked = input.OldRows.Where(r => r.Locked && r.PeriodEnd.Date >= eff).Select(r => (DateTime?)r.PeriodEnd.Date).ToList();
            plan.InvoicedUntil = locked.Count > 0 ? locked.Max() : null;

            // output rows
            foreach (var row in rows)
            {
                bool prepaid = row.PaidRent != null || row.PaidTax != null;
                plan.Rows.Add(new TransferPlanRowDto
                {
                    RowNo = row.RowNo, Label = row.Label, PeriodStart = row.PeriodStart, PeriodEnd = row.PeriodEnd, MonthFraction = row.MonthFraction,
                    MonthlyRent = row.MonthlyRent, ServiceCharge = row.ServiceCharge, DiscountType = row.DiscountType, DiscountValue = row.DiscountValue,
                    DiscountAmount = row.DiscountAmount, RentAfterDiscount = row.RentAfterDiscount, Tax = row.Tax,
                    AlreadyPaid = R2((row.PaidRent ?? 0m) + (row.PaidTax ?? 0m)), Difference = BookingScheduleMath.Difference(row),
                    SecurityDeposit = row.SecurityDeposit, ToBill = BookingScheduleMath.Billed(row).Total,
                    Prepaid = prepaid, TransferSettled = row.TransferSettled, OldInvoiceNumber = row.InvoiceNumber
                });
            }
            plan.Totals = new TransferPlanTotalsDto
            {
                MonthlyRent = R2(plan.Rows.Sum(r => r.MonthlyRent)), DiscountAmount = R2(plan.Rows.Sum(r => r.DiscountAmount)),
                RentAfterDiscount = R2(plan.Rows.Sum(r => r.RentAfterDiscount)), AlreadyPaid = R2(plan.Rows.Sum(r => r.AlreadyPaid)),
                Difference = R2(plan.Rows.Sum(r => r.Difference)), Tax = R2(plan.Rows.Sum(r => r.Tax)),
                SecurityDeposit = R2(plan.Rows.Sum(r => r.SecurityDeposit ?? 0m)), ToBill = R2(plan.Rows.Sum(r => r.ToBill))
            };
            res.Rows = rows;
            return res;
        }

        /// <summary>The @ScheduleJson array for WN_Booking_TransferSpace (camelCase, dates yyyy-MM-dd).</summary>
        public static string ToScheduleJson(IEnumerable<BookingScheduleRow> rows)
        {
            var list = rows.OrderBy(r => r.RowNo).Select(r => new
            {
                rowNo = r.RowNo,
                periodStart = r.PeriodStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                periodEnd = r.PeriodEnd.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                monthFraction = r.MonthFraction, monthlyRent = r.MonthlyRent, serviceCharge = r.ServiceCharge, roomRent = r.RoomRent,
                discountType = r.DiscountType, discountValue = r.DiscountValue, discountAmount = r.DiscountAmount,
                rentAfterDiscount = r.RentAfterDiscount, tax = r.Tax, securityDeposit = r.SecurityDeposit, total = r.Total,
                paidRent = r.PaidRent, paidServiceCharge = r.PaidServiceCharge, paidTax = r.PaidTax, transferSettled = r.TransferSettled
            });
            return System.Text.Json.JsonSerializer.Serialize(list);
        }
    }
}
