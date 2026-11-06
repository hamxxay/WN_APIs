using System;
using WorkNest.Application.DTOs.Transfer;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Calculates prorated settlement adjustment for space changes:
    /// dailyRate(rate) = rate / 30
    /// daysUsed = effectiveDate - periodStart
    /// remainingDays = periodEnd - effectiveDate
    /// creditAtA = remainingDays x dailyRate(A)
    /// repricedAtB = remainingDays x dailyRate(B)
    /// adjustment = repricedAtB - creditAtA
    /// </summary>
    public static class TransferSettlementCalculator
    {
        public static decimal DailyRate(decimal rate) => rate / 30.0m;

        public static TransferSettlementResult Calculate(
            DateTime periodStart,
            DateTime periodEnd,
            DateTime effectiveDate,
            decimal oldMonthlyRateA,
            decimal newMonthlyRateB,
            decimal transferFee = 0m)
        {
            var pStart = periodStart.Date;
            var pEnd = periodEnd.Date;
            var effDate = effectiveDate.Date;

            int daysUsed = Math.Max(0, (int)(effDate - pStart).TotalDays);
            int remainingDays = Math.Max(0, (int)(pEnd - effDate).TotalDays);

            decimal dailyA = DailyRate(oldMonthlyRateA);
            decimal dailyB = DailyRate(newMonthlyRateB);

            decimal creditAtA = Math.Round(remainingDays * dailyA, 2, MidpointRounding.AwayFromZero);
            decimal repricedAtB = Math.Round(remainingDays * dailyB, 2, MidpointRounding.AwayFromZero);
            decimal adjustment = repricedAtB - creditAtA;

            string outcome;
            string description;
            decimal totalDue = 0m;

            if (adjustment == 0m)
            {
                outcome = "NoAdjustment";
                totalDue = transferFee > 0m ? transferFee : 0m;
                description = transferFee > 0m
                    ? $"Rates are identical for the remaining term. Transfer fee of PKR {transferFee:N0} will be invoiced."
                    : "No rate adjustment required for the remaining term.";
            }
            else if (adjustment > 0m)
            {
                outcome = "Billed";
                totalDue = adjustment + (transferFee > 0m ? transferFee : 0m);
                description = $"Higher rate space: PKR {adjustment:N2} prorated difference for {remainingDays} remaining day(s) will be invoiced"
                    + (transferFee > 0m ? $" plus PKR {transferFee:N0} transfer fee (Total: PKR {totalDue:N2})." : ".");
            }
            else // adjustment < 0
            {
                outcome = "CreditStored";
                decimal credit = Math.Abs(adjustment);
                totalDue = transferFee > 0m ? transferFee : 0m;
                description = $"Lower rate space: PKR {credit:N2} credit for {remainingDays} remaining day(s) will be credited to the new booking"
                    + (transferFee > 0m ? $". Transfer fee of PKR {transferFee:N0} will be invoiced." : ".");
            }

            return new TransferSettlementResult
            {
                PeriodStart = pStart,
                PeriodEnd = pEnd,
                EffectiveDate = effDate,
                OldMonthlyRate = oldMonthlyRateA,
                NewMonthlyRate = newMonthlyRateB,
                DaysUsed = daysUsed,
                RemainingDays = remainingDays,
                CreditAtA = creditAtA,
                RepricedAtB = repricedAtB,
                Adjustment = adjustment,
                TransferFee = transferFee,
                TotalDue = totalDue,
                Outcome = outcome,
                OutcomeDescription = description
            };
        }
    }
}

