using System;
using WorkNest.Application.DTOs.Quotation;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// A quotation's security deposit as the quotation PDF and the lease agreement print it (SecurityDepositRules, "reading
    /// a deposit later"): the stored amount; a stored 0 with months on an older private-office row = months x monthly rent
    /// less the rent discount; otherwise none. Nothing turns 0 months into a default, and no amount typed elsewhere counts.
    /// </summary>
    public static class QuotationDeposit
    {
        /// <summary>
        /// Amount 0 = no deposit (no row, line or note). Months = the stored Security Deposit (Months) when they account for
        /// that amount, else 0 (older rows saved with a typed amount): documents then print the amount without months.
        /// </summary>
        public static (decimal Amount, int Months) Of(QuotationResponse q)
        {
            var kind = SecurityDepositRules.KindOf(q.SpaceType, q.SpaceTypeName);
            decimal rent = q.MonthlyBasePrice is > 0m ? q.MonthlyBasePrice.Value : q.MonthlyRent;
            decimal discountValue = SecurityDepositRules.IsFixedDiscount(q.DiscountType) ? q.DiscountValue : q.DiscountPercentage;
            decimal fraction = SecurityDepositRules.DiscountFraction(q.DiscountType, discountValue, rent);

            decimal amount = SecurityDepositRules.Stored(kind, q.SecurityDeposit, q.SecurityDepositMonths, rent, fraction);
            if (amount <= 0m) return (0m, 0);

            int months = q.SecurityDepositMonths;
            bool monthsFit = months > 0 && SecurityDepositRules.IsValidMonths(months)
                             && Math.Abs(SecurityDepositRules.Amount(months, rent, fraction) - amount) <= 1m;
            return (amount, monthsFit ? months : 0);
        }
    }
}
