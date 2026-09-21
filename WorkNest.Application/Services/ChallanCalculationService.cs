using System;
using System.Collections.Generic;
using System.Linq;
using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.DTOs.Quotation;

namespace WorkNest.Application.Services
{
        public class BookingFinancialSnapshot
    {
        public string BookingGuid { get; set; } = string.Empty;
        public string ChallanNumber { get; set; } = string.Empty;
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public string CalculatedDuration { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal BaseAmount { get; set; }
        public decimal SupportServiceAmount { get; set; }
        public decimal TaxableAmount { get; set; }
        public decimal PstRate { get; set; } = 16.00m;
        public decimal PstAmount { get; set; }
        public decimal SecurityDeposit { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string BillingUnit { get; set; } = "Per Day";
        public string SpaceType { get; set; } = "MeetingRoom";
    }

    public class ChallanCalculationInput
    {
        public int SpaceTypeId { get; set; }
        public int Capacity { get; set; } = 1;
        public decimal Price { get; set; } = 0m;
        public string? CategoryCode { get; set; }
        public string? SpaceTypeName { get; set; }
        public string? SpaceCode { get; set; }
        public string? SpaceName { get; set; }
        public DateTime? StartOn { get; set; }
        public DateTime? EndOn { get; set; }
        public decimal SubtotalAmount { get; set; }
        public decimal TotalContractAmount { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal CurrentCycleAmount { get; set; }
        public int ContractPeriodMonths { get; set; } = 12;
        public int BillingPeriodMonths { get; set; } = 3;
        public decimal SecurityDeposit { get; set; }
        public decimal PerSeatSupportRate { get; set; } = 2000.00m;
        public decimal AppliedChargePercentage { get; set; } = 10.00m;
        public decimal AppliedTaxPercentage { get; set; } = 16.00m;
        public string DiscountType { get; set; } = "Percentage";
        public decimal DiscountPercentage { get; set; } = 0m;
        public decimal DiscountValue { get; set; } = 0m;
        public decimal DiscountAmount { get; set; } = 0m;
        public decimal HourlyRate { get; set; } = 0m;
        public decimal DailyRate { get; set; } = 0m;
        public decimal SeatPrice { get; set; } = 0m;
        public decimal RoomPrice { get; set; } = 0m;
        public string? ExplicitBillingType { get; set; }
        public List<ChallanLineDto>? Details { get; set; }
        public List<QuotationDetailDto>? QuotationDetails { get; set; }
    }

    public class ChallanCalculationResult
    {
        public string SpaceType { get; set; } = "MeetingRoom";
        public string BillingType { get; set; } = "Hourly";
        public string BillingBasis { get; set; } = "Per Hour";
        public decimal BaseRent { get; set; }
        public decimal TotalContractRent { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal FirstCycleRent { get; set; }
        public int ContractPeriodMonths { get; set; }
        public int BillingPeriodMonths { get; set; }
        public decimal SecurityDeposit { get; set; }
        public decimal PerSeatSupportRate { get; set; } = 2000.00m;
        public decimal AppliedChargePercentage { get; set; } = 10.00m;
        public decimal SupportChargeAmount { get; set; }
        public decimal AppliedTaxPercentage { get; set; } = 16.00m;
        public decimal TaxAmount { get; set; }
        public decimal TaxAmountOnAdvanceRent { get; set; }
        public decimal TaxAmountOnContract { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TotalPayable { get; set; }
        public bool IsProrated { get; set; }
        public decimal ProratedCurrentMonthAmount { get; set; }
        public List<ChallanFieldDto> Fields { get; set; } = new();
    }

    public static class ChallanCalculationService
    {
        public static ChallanCalculationResult Calculate(ChallanCalculationInput input)
        {
            var res = new ChallanCalculationResult();

            string stName = input.SpaceTypeName ?? "";
            string catCode = input.CategoryCode ?? "";
            bool isMeetingRoom = stName.Contains("Meeting", StringComparison.OrdinalIgnoreCase) ||
                                 stName.Contains("Conference", StringComparison.OrdinalIgnoreCase) ||
                                 catCode.Contains("Meeting", StringComparison.OrdinalIgnoreCase) ||
                                 (input.BillingPeriodMonths <= 0 && input.TotalContractAmount <= 0);

            bool isPrivate = stName.Contains("Private", StringComparison.OrdinalIgnoreCase) ||
                             stName.Contains("Office", StringComparison.OrdinalIgnoreCase) ||
                             stName.Contains("Room", StringComparison.OrdinalIgnoreCase) ||
                             catCode.Contains("Private", StringComparison.OrdinalIgnoreCase) ||
                             input.SpaceTypeId == 1;

            if (isMeetingRoom)
            {
                res.SpaceType = "MeetingRoom";
            }
            else if (isPrivate)
            {
                res.SpaceType = "PrivateRoom";
            }
            else
            {
                res.SpaceType = "SharedSpace";
            }

            res.AppliedChargePercentage = input.AppliedChargePercentage > 0 ? input.AppliedChargePercentage : 10.00m;
            res.AppliedTaxPercentage = input.AppliedTaxPercentage > 0 ? input.AppliedTaxPercentage : 16.00m;

            if (res.SpaceType == "MeetingRoom")
            {
                double totalHours = 0;
                if (input.StartOn.HasValue && input.EndOn.HasValue)
                {
                    totalHours = Math.Ceiling((input.EndOn.Value - input.StartOn.Value).TotalHours);
                }
                if (totalHours <= 0) totalHours = 1;

                if (!string.IsNullOrWhiteSpace(input.ExplicitBillingType))
                {
                    res.BillingType = input.ExplicitBillingType;
                }
                else if (totalHours >= 24 || (input.DailyRate > 0 && input.HourlyRate == 0))
                {
                    res.BillingType = "Daily";
                }
                else
                {
                    res.BillingType = "Hourly";
                }

                if (res.BillingType == "Daily")
                {
                    res.BillingBasis = "Per Day";
                }
                else if (res.BillingType == "Hourly")
                {
                    res.BillingBasis = "Per Hour";
                }
                else
                {
                    res.BillingBasis = "Per Booking Slot";
                }

                res.MonthlyRent = 0;
                res.ContractPeriodMonths = 1;
                res.BillingPeriodMonths = 1;
                res.SecurityDeposit = 0;

                res.BaseRent = input.SubtotalAmount > 0 ? input.SubtotalAmount : input.CurrentCycleAmount;
                res.FirstCycleRent = res.BaseRent;
                res.TotalContractRent = res.BaseRent;

                res.DiscountAmount = input.DiscountAmount;
                decimal discountedMeetingBase = Math.Max(0, res.BaseRent - res.DiscountAmount);
                res.SupportChargeAmount = Math.Round(discountedMeetingBase * (res.AppliedChargePercentage / 100.0m), 2);
                res.TaxAmount = Math.Round(res.SupportChargeAmount * (res.AppliedTaxPercentage / 100.0m), 2);
                res.TaxAmountOnAdvanceRent = res.TaxAmount;
                res.TaxAmountOnContract = res.TaxAmount;

                res.TotalPayable = Math.Max(0, res.BaseRent + res.TaxAmount - res.DiscountAmount);
            }
            else
            {
                res.BillingType = "Monthly";
                res.BillingBasis = "Per Month";
                res.ContractPeriodMonths = input.ContractPeriodMonths > 0 ? input.ContractPeriodMonths : 12;
                res.BillingPeriodMonths = input.BillingPeriodMonths > 0 ? input.BillingPeriodMonths : 3;

                if (input.MonthlyRent > 0)
                {
                    res.MonthlyRent = input.MonthlyRent;
                }
                else
                {
                    decimal price = input.Price > 0 ? input.Price :
                        (input.RoomPrice > 0 ? input.RoomPrice :
                        (input.SeatPrice > 0 ? input.SeatPrice : 0));
                    int cap = input.Capacity > 0 ? input.Capacity : 1;

                    if (res.SpaceType == "PrivateRoom" || input.SpaceTypeId == 1)
                    {
                        res.MonthlyRent = price * cap;
                    }
                    else
                    {
                        res.MonthlyRent = price;
                    }
                }

                if (res.MonthlyRent == 0 && input.SubtotalAmount > 0 && res.ContractPeriodMonths > 0)
                {
                    res.MonthlyRent = input.SubtotalAmount / res.ContractPeriodMonths;
                }

                res.TotalContractRent = (input.TotalContractAmount > 0 && input.TotalContractAmount >= (res.MonthlyRent * res.ContractPeriodMonths))
                    ? input.TotalContractAmount
                    : (res.MonthlyRent * res.ContractPeriodMonths);

                int capacity = input.Capacity > 0 ? input.Capacity : 1;
                decimal supportRate = input.PerSeatSupportRate > 0 ? input.PerSeatSupportRate : 2000.00m;
                res.PerSeatSupportRate = supportRate;

                // Proration calculation (Task 7)
                if (input.StartOn.HasValue && input.StartOn.Value.Day > 1)
                {
                    int startDay = input.StartOn.Value.Day;
                    int daysInMonth = DateTime.DaysInMonth(input.StartOn.Value.Year, input.StartOn.Value.Month);
                    int remainingDays = daysInMonth - startDay + 1;
                    decimal proratedCurrentMonth = Math.Round(((decimal)remainingDays / daysInMonth) * res.MonthlyRent, 2);
                    res.IsProrated = true;
                    res.ProratedCurrentMonthAmount = proratedCurrentMonth;

                    if (startDay < 15)
                    {
                        // Current month counts as first billing month: prorated current + (N - 1) full months
                        int fullMonths = Math.Max(0, res.BillingPeriodMonths - 1);
                        res.FirstCycleRent = proratedCurrentMonth + (fullMonths * res.MonthlyRent);
                    }
                    else
                    {
                        // Current month is a separate prorated period: prorated current + N full months
                        res.FirstCycleRent = proratedCurrentMonth + (res.BillingPeriodMonths * res.MonthlyRent);
                    }
                }
                else
                {
                    res.FirstCycleRent = (res.MonthlyRent > 0 && res.BillingPeriodMonths > 0)
                        ? (res.MonthlyRent * res.BillingPeriodMonths)
                        : (input.CurrentCycleAmount > 0 ? input.CurrentCycleAmount : res.MonthlyRent);
                }

                res.BaseRent = res.FirstCycleRent;

                decimal initialDeposit = (input.SecurityDeposit > 0 || res.SpaceType == "PrivateRoom") ? input.SecurityDeposit : 0;

                decimal discountPct = input.DiscountPercentage > 0 ? input.DiscountPercentage :
                    (string.Equals(input.DiscountType, "Percentage", StringComparison.OrdinalIgnoreCase) ? input.DiscountValue : 0);

                decimal discountOnRent = 0m;

                if (discountPct > 0)
                {
                    discountOnRent = Math.Round(res.FirstCycleRent * (discountPct / 100.0m), 2);
                }
                else
                {
                    decimal fixedVal = input.DiscountAmount > 0 ? input.DiscountAmount :
                        ((string.Equals(input.DiscountType, "Amount", StringComparison.OrdinalIgnoreCase) || string.Equals(input.DiscountType, "Fixed", StringComparison.OrdinalIgnoreCase)) ? input.DiscountValue : 0m);

                    if (fixedVal > 0)
                    {
                        discountOnRent = Math.Min(fixedVal, res.FirstCycleRent);
                    }
                }

                decimal discountedRent = Math.Max(0, res.FirstCycleRent - discountOnRent);

                // Task 1: Security Deposit Discount Mirroring
                decimal depositDiscountPct = discountPct > 0 ? discountPct :
                    (res.FirstCycleRent > 0 && discountOnRent > 0 ? (discountOnRent / res.FirstCycleRent) * 100m : 0m);

                decimal discountedDeposit = initialDeposit > 0
                    ? Math.Max(0, Math.Round(initialDeposit * (1 - (depositDiscountPct / 100.0m)), 2))
                    : 0m;

                res.SecurityDeposit = discountedDeposit;
                res.DiscountAmount = discountOnRent;

                // Task 2: Flat Per-Seat Support Charge based on room total seat capacity
                res.SupportChargeAmount = Math.Round(supportRate * capacity * res.BillingPeriodMonths, 2);
                res.TaxAmount = Math.Round(res.SupportChargeAmount * (res.AppliedTaxPercentage / 100.0m), 2);
                res.TaxAmountOnAdvanceRent = res.TaxAmount;
                res.TaxAmountOnContract = Math.Round(supportRate * capacity * res.ContractPeriodMonths * (res.AppliedTaxPercentage / 100.0m), 2);

                res.TotalPayable = Math.Max(0, discountedRent + discountedDeposit + res.TaxAmount);
            }

            BuildFields(res, input);
            return res;
        }

        private static void BuildFields(ChallanCalculationResult res, ChallanCalculationInput input)
        {
            var fields = new List<ChallanFieldDto>();

            string spaceNo = !string.IsNullOrWhiteSpace(input.SpaceCode) ? input.SpaceCode : (!string.IsNullOrWhiteSpace(input.SpaceName) ? input.SpaceName : "");
            fields.Add(new ChallanFieldDto { Key = "spaceNo", Label = "Space No.", Value = spaceNo, IsHeader = true });

            if (!string.IsNullOrWhiteSpace(input.SpaceName))
            {
                fields.Add(new ChallanFieldDto { Key = "spaceName", Label = "Space Name", Value = input.SpaceName, IsHeader = true });
            }

            if (res.SpaceType == "MeetingRoom")
            {
                string periodStr = "";
                if (input.StartOn.HasValue && input.EndOn.HasValue)
                {
                    periodStr = $"{input.StartOn.Value:dd MMM yyyy hh:mm tt} - {input.EndOn.Value:dd MMM yyyy hh:mm tt}";
                }
                if (!string.IsNullOrWhiteSpace(periodStr))
                {
                    fields.Add(new ChallanFieldDto { Key = "contractPeriod", Label = "Contract/Booking Period", Value = periodStr });
                }

                if (input.StartOn.HasValue)
                {
                    fields.Add(new ChallanFieldDto { Key = "startDateTime", Label = "Start Date & Time", Value = input.StartOn.Value.ToString("dd MMM yyyy hh:mm tt") });
                }

                if (input.EndOn.HasValue)
                {
                    fields.Add(new ChallanFieldDto { Key = "endDateTime", Label = "End Date & Time", Value = input.EndOn.Value.ToString("dd MMM yyyy hh:mm tt") });
                }

                if (input.StartOn.HasValue && input.EndOn.HasValue)
                {
                    var timeSpan = input.EndOn.Value - input.StartOn.Value;
                    if (res.BillingType == "Daily")
                    {
                        int days = (int)Math.Max(1, Math.Round(timeSpan.TotalDays));
                        fields.Add(new ChallanFieldDto { Key = "duration", Label = "Duration", Value = $"{days} Day{(days > 1 ? "s" : "")}" });
                    }
                    else if (res.BillingType == "Hourly")
                    {
                        double totalHours = Math.Ceiling(timeSpan.TotalHours);
                        int hours = (int)Math.Max(1, totalHours);
                        fields.Add(new ChallanFieldDto { Key = "duration", Label = "Duration", Value = $"{hours} Hour{(hours > 1 ? "s" : "")}" });
                    }
                }

                fields.Add(new ChallanFieldDto { Key = "billingBasis", Label = "Billing Basis", Value = res.BillingBasis });
                fields.Add(new ChallanFieldDto { Key = "rentAmount", Label = "Rent/Booking Amount", Value = FormatCurrency(res.BaseRent) });

                if (res.SupportChargeAmount > 0)
                {
                    fields.Add(new ChallanFieldDto { Key = "supportCharge", Label = "Support & Service Charges", Value = FormatCurrency(res.SupportChargeAmount) });
                }

                if (res.TaxAmount > 0)
                {
                    fields.Add(new ChallanFieldDto { Key = "tax", Label = $"Provincial Sales Tax ({res.AppliedTaxPercentage:G29}% on Support)", Value = FormatCurrency(res.TaxAmount) });
                }

                if (res.DiscountAmount > 0)
                {
                    fields.Add(new ChallanFieldDto { Key = "discount", Label = "Discount", Value = FormatCurrency(res.DiscountAmount) });
                }

                fields.Add(new ChallanFieldDto { Key = "totalAmount", Label = "TOTAL INITIAL AMOUNT PAYABLE", Value = FormatCurrency(res.TotalPayable) });
            }
            else
            {
                string contractPeriodStr = "";
                if (input.StartOn.HasValue && input.EndOn.HasValue)
                {
                    contractPeriodStr = $"{input.StartOn.Value:dd MMM yyyy} - {input.EndOn.Value:dd MMM yyyy}";
                }
                else if (res.ContractPeriodMonths > 0)
                {
                    contractPeriodStr = $"{res.ContractPeriodMonths} Month{(res.ContractPeriodMonths > 1 ? "s" : "")}";
                }
                if (!string.IsNullOrWhiteSpace(contractPeriodStr))
                {
                    fields.Add(new ChallanFieldDto { Key = "contractPeriod", Label = "Contract Period", Value = contractPeriodStr });
                }

                string bPeriodLabel = $"{res.BillingPeriodMonths} Month{(res.BillingPeriodMonths > 1 ? "s" : "")}";
                fields.Add(new ChallanFieldDto { Key = "billingPeriod", Label = "Billing Period", Value = bPeriodLabel });
                fields.Add(new ChallanFieldDto { Key = "advanceRentPeriod", Label = "Advance Rent Period", Value = bPeriodLabel });

                fields.Add(new ChallanFieldDto { Key = "advanceRent", Label = $"Advance Rent ({bPeriodLabel})", Value = FormatCurrency(res.FirstCycleRent) });
                fields.Add(new ChallanFieldDto { Key = "monthlyRent", Label = "Monthly Rent", Value = FormatCurrency(res.MonthlyRent) });

                if (res.SpaceType == "PrivateRoom" && res.SecurityDeposit > 0)
                {
                    fields.Add(new ChallanFieldDto { Key = "securityDeposit", Label = "Security Deposit (Refundable)", Value = FormatCurrency(res.SecurityDeposit) });
                }

                if (res.TaxAmount > 0)
                {
                    fields.Add(new ChallanFieldDto { Key = "tax", Label = "Provincial Sales Tax", Value = FormatCurrency(res.TaxAmount) });
                }

                if (res.DiscountAmount > 0)
                {
                    fields.Add(new ChallanFieldDto { Key = "discount", Label = "Discount", Value = FormatCurrency(res.DiscountAmount) });
                }

                fields.Add(new ChallanFieldDto { Key = "totalAmount", Label = "TOTAL INITIAL AMOUNT PAYABLE", Value = FormatCurrency(res.TotalPayable) });
            }

            res.Fields = fields;
        }

        public static void ApplyToQuotation(QuotationResponse q)
        {
            if (q == null) return;

            decimal effectiveSubtotal = q.SubtotalAmount;
            if (effectiveSubtotal <= 0 && q.Details != null && q.Details.Count > 0)
            {
                var rentLine = q.Details.FirstOrDefault(d => d.FeeType == "RoomRent");
                if (rentLine != null && rentLine.Amount > 0)
                {
                    effectiveSubtotal = rentLine.Amount;
                }
            }

            decimal secDeposit = q.SecurityDeposit;
            if (secDeposit <= 0 && q.Details != null && q.Details.Count > 0)
            {
                var secLine = q.Details.FirstOrDefault(d => string.Equals(d.FeeType, "SecurityDeposit", StringComparison.OrdinalIgnoreCase) || (d.Description != null && d.Description.Contains("Security Deposit", StringComparison.OrdinalIgnoreCase)));
                if (secLine != null && secLine.Amount > 0)
                {
                    secDeposit = secLine.Amount;
                }
            }
            if (secDeposit <= 0 && q.Contract != null && q.Contract.SecurityDeposit > 0)
            {
                secDeposit = q.Contract.SecurityDeposit;
            }

            var input = new ChallanCalculationInput
            {
                SpaceTypeName = q.SpaceTypeName,
                CategoryCode = q.SpaceType,
                SpaceCode = q.SpaceCode,
                SpaceName = q.SpaceName,
                StartOn = q.StartDateTime,
                EndOn = q.EndDateTime,
                SubtotalAmount = effectiveSubtotal,
                TotalContractAmount = q.TotalContractAmount > 0 ? q.TotalContractAmount : effectiveSubtotal,
                MonthlyRent = q.MonthlyRent,
                CurrentCycleAmount = effectiveSubtotal,
                ContractPeriodMonths = q.Contract?.NumberOfMonths > 0 ? q.Contract.NumberOfMonths : 12,
                BillingPeriodMonths = q.BillingPeriodMonths > 0 ? q.BillingPeriodMonths : 3,
                Capacity = q.Capacity ?? 1,
                PerSeatSupportRate = q.PerSeatSupportRate ?? 2000.00m,
                SecurityDeposit = secDeposit,
                AppliedChargePercentage = q.AppliedChargePercentage > 0 ? q.AppliedChargePercentage : 10.00m,
                AppliedTaxPercentage = q.AppliedTaxPercentage > 0 ? q.AppliedTaxPercentage : 16.00m,
                DiscountType = q.DiscountType ?? "Percentage",
                DiscountPercentage = q.DiscountPercentage,
                DiscountValue = q.DiscountValue,
                DiscountAmount = q.DiscountAmount,
                ExplicitBillingType = q.BillingType,
                QuotationDetails = q.Details
            };

            var calc = Calculate(input);

            q.SpaceType = calc.SpaceType;
            q.BillingType = calc.BillingType;
            q.SubtotalAmount = calc.FirstCycleRent;
            q.TotalContractAmount = calc.TotalContractRent;
            q.MonthlyRent = calc.MonthlyRent;
            q.CurrentCycleAmount = calc.FirstCycleRent;
            q.SecurityDeposit = calc.SecurityDeposit;
            q.PerSeatSupportRate = calc.PerSeatSupportRate;
            q.AppliedChargePercentage = calc.AppliedChargePercentage;
            q.SupportChargeAmount = calc.SupportChargeAmount;
            q.AppliedTaxPercentage = calc.AppliedTaxPercentage;
            q.TaxAmount = calc.TaxAmount;
            q.TaxAmountOnAdvanceRent = calc.TaxAmountOnAdvanceRent;
            q.TaxAmountOnContract = calc.TaxAmountOnContract;
            q.DiscountAmount = calc.DiscountAmount;
            q.TotalPayable = calc.TotalPayable;
            q.TotalAmount = calc.TotalPayable;
            q.Fields = calc.Fields;
        }

        public static void ApplyToChallan(ChallanResponseDto c)
        {
            if (c == null) return;

            decimal subtotalFromLines = 0;
            if (c.Details != null && c.Details.Count > 0)
            {
                var rentLines = c.Details
                    .Where(d => !d.ChargeTypeCode.Contains("TAX", StringComparison.OrdinalIgnoreCase)
                             && !d.ChargeTypeLabel.Contains("Tax", StringComparison.OrdinalIgnoreCase)
                             && !d.ChargeTypeCode.Contains("SERVICE", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (rentLines.Count > 0)
                {
                    subtotalFromLines = rentLines.Sum(d => d.LineTotal);
                }
            }

            decimal effectiveSubtotal = subtotalFromLines > 0 ? subtotalFromLines : c.SubtotalAmount;

            var input = new ChallanCalculationInput
            {
                SpaceTypeName = c.SpaceTypeName,
                SpaceCode = c.SpaceCode,
                SpaceName = c.SpaceName ?? c.SpaceNumber,
                StartOn = c.StartOn,
                EndOn = c.EndOn,
                SubtotalAmount = effectiveSubtotal,
                TotalContractAmount = c.TotalContractAmount,
                MonthlyRent = c.MonthlyRent,
                CurrentCycleAmount = 0,
                ContractPeriodMonths = c.Contract?.NumberOfMonths > 0 ? c.Contract.NumberOfMonths : (c.NumberOfMonths > 0 ? c.NumberOfMonths : 12),
                BillingPeriodMonths = c.BillingPeriodMonths > 0 ? c.BillingPeriodMonths : 3,
                SecurityDeposit = c.SecurityDeposit,
                AppliedChargePercentage = 10.00m,
                AppliedTaxPercentage = c.AppliedTaxPercentage > 0 ? c.AppliedTaxPercentage : 16.00m,
                DiscountPercentage = c.DiscountPercentage,
                DiscountAmount = c.DiscountAmount,
                Capacity = c.SpaceCapacity > 0 ? c.SpaceCapacity : 1,
                SeatPrice = c.SeatPrice,
                RoomPrice = c.RoomPrice,
                ExplicitBillingType = c.BillingType,
                Details = c.Details
            };

            var calc = Calculate(input);

            c.SpaceType = calc.SpaceType;
            c.BillingType = calc.BillingType;
            c.FirstCycleRent = calc.FirstCycleRent;
            c.SubtotalAmount = calc.FirstCycleRent;
            c.TotalContractAmount = calc.TotalContractRent;
            c.MonthlyRent = calc.MonthlyRent;
            c.CurrentCycleAmount = calc.FirstCycleRent;
            c.SecurityDeposit = calc.SecurityDeposit;
            c.SupportChargeAmount = calc.SupportChargeAmount;
            c.TaxAmount = calc.TaxAmount;
            c.TaxAmountOnAdvanceRent = calc.TaxAmountOnAdvanceRent;
            c.TaxAmountOnContract = calc.TaxAmountOnContract;
            c.DiscountAmount = calc.DiscountAmount;
            c.TotalPayable = calc.TotalPayable;
            c.Fields = calc.Fields;
        }

        private static string FormatCurrency(decimal amount)
        {
            return $"PKR {amount:N2}";
        }
    }
}