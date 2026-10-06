using System;

namespace WorkNest.Application.Services
{
    public class InvoiceCalculationRequest
    {
        public string? SpaceTypeName { get; set; }
        public string? CategoryCode { get; set; }
        public int SpaceTypeId { get; set; }
        public int Capacity { get; set; } = 1;
        public decimal MonthlyRent { get; set; }
        public decimal SeatPrice { get; set; }
        public decimal RoomPrice { get; set; }
        public decimal SubtotalAmount { get; set; }
        public decimal CurrentCycleAmount { get; set; }
        public int BillingPeriodMonths { get; set; } = 3;
        public int ContractPeriodMonths { get; set; } = 12;
        public decimal SecurityDeposit { get; set; }
        public int SecurityDepositMonths { get; set; } = 2;
        public int SecurityDepositInstallments { get; set; } = 1;
        public decimal PerSeatSupportRate { get; set; } = 2000.00m;
        public decimal AppliedChargePercentage { get; set; } = 10.00m;
        public decimal AppliedTaxPercentage { get; set; } = 16.00m;
        public string DiscountType { get; set; } = "Percentage";
        public decimal DiscountPercentage { get; set; } = 0m;
        public decimal DiscountAmount { get; set; } = 0m;
        public decimal DiscountValue { get; set; } = 0m;
        public DateTime? StartOn { get; set; }
        public DateTime? EndOn { get; set; }
        public string? ExplicitBillingType { get; set; }
        public bool DisableProration { get; set; } = false;
        public bool IsQuotation { get; set; } = false;
    }

    public class InvoiceCalculationResult
    {
        public bool IsMeetingRoom { get; set; }
        public string SpaceType { get; set; } = "PrivateRoom";
        public string BillingType { get; set; } = "Monthly";
        public string BillingBasis { get; set; } = "Per Month";

        public decimal MonthlyRent { get; set; }
        public int BillingPeriodMonths { get; set; }
        public int ContractPeriodMonths { get; set; }

        public bool IsProrated { get; set; }
        public int ProratedDays { get; set; }
        public int DaysInStartMonth { get; set; }
        public decimal ProratedCurrentMonthAmount { get; set; }
        public DateTime BillingPeriodStart { get; set; }
        public DateTime BillingPeriodEnd { get; set; }

        public decimal Rent { get; set; }
        public decimal Discount { get; set; }
        public decimal NetRent => Math.Max(0m, Rent - Discount);

        public decimal ServiceCharge { get; set; }
        public decimal Tax { get; set; }

        public decimal DepositBase { get; set; }
        public decimal DepositDiscount { get; set; }
        public decimal DepositAfterDiscount { get; set; }
        public int DepositInstallments { get; set; } = 1;
        public decimal DepositFirstInstallment { get; set; }

        public decimal RoomRentExclTax => Math.Max(0m, NetRent - ServiceCharge);
        public decimal GrandTotal { get; set; }
    }

    public static class InvoiceCalculationEngine
    {
        private static decimal Round2(decimal val) =>
            Math.Round(val, 2, MidpointRounding.AwayFromZero);

        public static InvoiceCalculationResult CalculateInvoice(InvoiceCalculationRequest request)
            => CalculateInvoice(request, BusinessClock.Default.Today);

        /// <summary>Same calculation with the business "today" passed in (used when the booking has no start date).</summary>
        public static InvoiceCalculationResult CalculateInvoice(InvoiceCalculationRequest request, DateTime today)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var result = new InvoiceCalculationResult();

            string stName = request.SpaceTypeName ?? "";
            string catCode = request.CategoryCode ?? "";
            bool isMeeting = stName.Contains("Meeting", StringComparison.OrdinalIgnoreCase) ||
                            stName.Contains("Conference", StringComparison.OrdinalIgnoreCase) ||
                            catCode.Contains("Meeting", StringComparison.OrdinalIgnoreCase) ||
                            (request.BillingPeriodMonths <= 0 && request.SubtotalAmount <= 0 && request.MonthlyRent <= 0);

            result.IsMeetingRoom = isMeeting;
            result.SpaceType = isMeeting ? "MeetingRoom" : (stName.Contains("Private", StringComparison.OrdinalIgnoreCase) || catCode.Contains("Private", StringComparison.OrdinalIgnoreCase) || request.SpaceTypeId == 1 ? "PrivateRoom" : "SharedSpace");

            int capacity = request.Capacity > 0 ? request.Capacity : 1;
            decimal chargeRate = request.AppliedChargePercentage > 0 ? request.AppliedChargePercentage : 10.00m;
            decimal taxRate = request.AppliedTaxPercentage > 0 ? request.AppliedTaxPercentage : 16.00m;

            DateTime periodStart = request.StartOn ?? today.Date;
            DateTime periodEnd;

            if (isMeeting)
            {
                result.BillingType = !string.IsNullOrWhiteSpace(request.ExplicitBillingType) ? request.ExplicitBillingType : "Hourly";
                result.BillingBasis = result.BillingType == "Daily" ? "Per Day" : "Per Hour";
                result.MonthlyRent = 0m;
                result.BillingPeriodMonths = 1;
                result.ContractPeriodMonths = 1;
                periodEnd = request.EndOn ?? periodStart;

                decimal baseRent = request.SubtotalAmount > 0 ? request.SubtotalAmount : (request.CurrentCycleAmount > 0 ? request.CurrentCycleAmount : 0m);
                result.Rent = Round2(baseRent);

                decimal discount = 0m;
                if (request.DiscountPercentage > 0)
                {
                    discount = Round2(result.Rent * (request.DiscountPercentage / 100.0m));
                }
                else if (request.DiscountAmount > 0)
                {
                    discount = Round2(Math.Min(request.DiscountAmount, result.Rent));
                }
                else if (request.DiscountValue > 0)
                {
                    if (string.Equals(request.DiscountType, "Percentage", StringComparison.OrdinalIgnoreCase))
                        discount = Round2(result.Rent * (request.DiscountValue / 100.0m));
                    else
                        discount = Round2(Math.Min(request.DiscountValue, result.Rent));
                }
                result.Discount = discount;

                decimal discountedBase = result.NetRent;
                result.ServiceCharge = Round2(discountedBase * (chargeRate / 100.0m));
                result.Tax = Round2(result.ServiceCharge * (taxRate / 100.0m));

                result.DepositBase = 0m;
                result.DepositDiscount = 0m;
                result.DepositAfterDiscount = 0m;
                result.DepositFirstInstallment = 0m;

                result.BillingPeriodStart = periodStart;
                result.BillingPeriodEnd = periodEnd;
                result.GrandTotal = Round2(result.NetRent + result.Tax);
                return result;
            }

            // Private Office or Dedicated Desk
            result.BillingType = "Monthly";
            result.BillingBasis = "Per Month";

            int billingMonths = request.BillingPeriodMonths > 0 ? request.BillingPeriodMonths : 3;
            int contractMonths = request.ContractPeriodMonths > 0 ? request.ContractPeriodMonths : 12;
            result.BillingPeriodMonths = billingMonths;
            result.ContractPeriodMonths = contractMonths;

            decimal monthlyRent = request.MonthlyRent;
            if (monthlyRent <= 0)
            {
                if (request.RoomPrice > 0) monthlyRent = request.RoomPrice;
                else if (request.SeatPrice > 0) monthlyRent = request.SeatPrice * capacity;
                else if (request.SubtotalAmount > 0 && contractMonths > 0) monthlyRent = Round2(request.SubtotalAmount / contractMonths);
            }
            result.MonthlyRent = Round2(monthlyRent);

            decimal effectiveMonths = billingMonths;

            // Proration Logic
            if (!request.DisableProration && !request.IsQuotation && request.StartOn.HasValue && request.StartOn.Value.Day > 1 && result.MonthlyRent > 0)
            {
                int startDay = request.StartOn.Value.Day;
                int daysInMonth = DateTime.DaysInMonth(request.StartOn.Value.Year, request.StartOn.Value.Month);
                // Days are counted from the day AFTER the start (signing) day: 20th of a 30-day month = 10 days.
                // Before the 15th: those days (+ the rest of the billing cycle); from the 15th: days + full cycle.
                int remainingDays = daysInMonth - startDay;
                decimal fraction = (decimal)remainingDays / daysInMonth;
                periodStart = request.StartOn.Value.Date.AddDays(1);
                decimal proratedCurrentMonth = Round2(fraction * result.MonthlyRent);

                result.IsProrated = true;
                result.ProratedDays = remainingDays;
                result.DaysInStartMonth = daysInMonth;
                result.ProratedCurrentMonthAmount = proratedCurrentMonth;

                if (startDay < 15)
                {
                    effectiveMonths = billingMonths <= 1 ? fraction : fraction + (billingMonths - 1);
                    periodEnd = new DateTime(request.StartOn.Value.Year, request.StartOn.Value.Month, 1).AddMonths(billingMonths).AddDays(-1);
                }
                else
                {
                    effectiveMonths = fraction + billingMonths;
                    periodEnd = new DateTime(request.StartOn.Value.Year, request.StartOn.Value.Month, 1).AddMonths(billingMonths + 1).AddDays(-1);
                }
            }
            else
            {
                periodEnd = periodStart.AddMonths(billingMonths).AddDays(-1);
            }

            result.Rent = Round2(result.MonthlyRent * effectiveMonths);
            result.BillingPeriodStart = periodStart;
            result.BillingPeriodEnd = periodEnd;

            // Rent Discount
            decimal discountPct = request.DiscountPercentage > 0 ? request.DiscountPercentage :
                (string.Equals(request.DiscountType, "Percentage", StringComparison.OrdinalIgnoreCase) ? request.DiscountValue : 0m);

            decimal discountAmount = 0m;
            if (discountPct > 0)
            {
                discountAmount = Round2(result.Rent * (discountPct / 100.0m));
            }
            else
            {
                decimal fixedDisc = request.DiscountAmount > 0 ? request.DiscountAmount :
                    ((string.Equals(request.DiscountType, "Amount", StringComparison.OrdinalIgnoreCase) || string.Equals(request.DiscountType, "Fixed", StringComparison.OrdinalIgnoreCase)) ? request.DiscountValue : 0m);

                if (fixedDisc > 0)
                {
                    if (isMeeting)
                    {
                        discountAmount = Round2(Math.Min(fixedDisc, result.Rent));
                    }
                    else
                    {
                        // PKR fixedDisc is per month, so for the billing period cycle: fixedDisc * billingMonths
                        discountAmount = Round2(Math.Min(fixedDisc * billingMonths, result.Rent));
                    }

                    decimal basisForPct = result.Rent;
                    if (basisForPct > 0)
                    {
                        discountPct = (discountAmount / basisForPct) * 100.0m;
                    }
                }
            }
            result.Discount = discountAmount;

            // Support services: PKR 2,000 (per-seat support rate) × Capacity × Billing Months
            decimal perSeatSupportRate = request.PerSeatSupportRate > 0 ? request.PerSeatSupportRate : 2000.00m;
            result.ServiceCharge = Round2(perSeatSupportRate * capacity * billingMonths);
            // Tax: 16% on service charge only
            result.Tax = Round2(result.ServiceCharge * (taxRate / 100.0m));

            // Security Deposit Calculation (Full amount / months, never prorated)
            decimal baseDeposit = 0m;
            if (request.SecurityDeposit > 0)
            {
                baseDeposit = request.SecurityDeposit;
                result.DepositBase = baseDeposit;
                result.DepositDiscount = 0m;
                result.DepositAfterDiscount = baseDeposit;
            }
            else
            {
                int secMonths = request.SecurityDepositMonths > 0 ? request.SecurityDepositMonths : 2;
                if (secMonths > 0 && result.MonthlyRent > 0)
                {
                    baseDeposit = Round2(result.MonthlyRent * secMonths);
                }
                result.DepositBase = baseDeposit;

                decimal depositDiscount = 0m;
                if (baseDeposit > 0 && discountPct > 0)
                {
                    depositDiscount = Round2(baseDeposit * (discountPct / 100.0m));
                }
                result.DepositDiscount = depositDiscount;
                result.DepositAfterDiscount = Round2(Math.Max(0m, baseDeposit - depositDiscount));
            }

            // Deposit Installments
            int installments = request.SecurityDepositInstallments > 0 ? request.SecurityDepositInstallments : 1;
            result.DepositInstallments = installments;
            result.DepositFirstInstallment = installments > 1 
                ? Round2(result.DepositAfterDiscount / installments) 
                : result.DepositAfterDiscount;

            // GrandTotal = Rent - Discount + Tax + DepositAfterDiscount (or First Installment if enabled)
            result.GrandTotal = Round2(result.NetRent + result.Tax + result.DepositFirstInstallment);

            return result;
        }
    }
}
