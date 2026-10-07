using System;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Withholding Tax (WHT) invoice gross-up. Reference implementation of the formula in WN_Invoices_Insert
    /// (step 11b), WN_UpdateInvoiceBreakdown and WN_Invoice_ApplyWhtToLines; keep them identical.
    /// Each component is grossed up so the net received after the customer withholds the rate is unchanged:
    /// Gross = ROUND(Net / (1 - rate/100), 2), i.e. Net * k with k = 1 / (1 - rate/100). WhtAmount is derived by
    /// subtraction so the voucher always balances. The security deposit is never grossed up.
    /// Rounding is half away from zero, the same as SQL Server ROUND.
    /// </summary>
    public static class WhtCalculator
    {
        public sealed record Result(
            decimal RentGross,
            decimal ServiceGross,
            decimal TaxGross,
            decimal InvoiceTotal,
            decimal NetAmount,
            decimal WhtAmount);

        public static Result Calculate(decimal rentExclServiceCharges, decimal serviceCharges, decimal taxOnServiceCharges, decimal whtRatePercent)
        {
            if (whtRatePercent <= 0m || whtRatePercent >= 100m)
                throw new ArgumentOutOfRangeException(nameof(whtRatePercent), "WHT rate must be greater than 0 and less than 100.");

            decimal divisor = 1m - whtRatePercent / 100m;
            decimal rentGross = Gross(rentExclServiceCharges, divisor);
            decimal serviceGross = Gross(serviceCharges, divisor);
            decimal taxGross = Gross(taxOnServiceCharges, divisor);

            decimal invoiceTotal = rentGross + serviceGross + taxGross;
            decimal netAmount = rentExclServiceCharges + serviceCharges + taxOnServiceCharges;
            return new Result(rentGross, serviceGross, taxGross, invoiceTotal, netAmount, invoiceTotal - netAmount);
        }

        private static decimal Gross(decimal net, decimal divisor) =>
            Math.Round(net / divisor, 2, MidpointRounding.AwayFromZero);
    }
}
