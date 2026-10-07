using System;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Withholding Tax (WHT) invoice gross-up. Reference implementation of the formula in WN_Invoices_Insert
    /// (step 11b) and WN_UpdateInvoiceBreakdown; keep them identical.
    /// Room rent (rent excluding support + the support charge) is grossed up as ONE amount so the net received after
    /// the customer withholds the rate is unchanged: GrossRoomRent = ROUND(RoomRentNet / (1 - rate/100), 2).
    /// The rent line absorbs the whole increase (RentLine = GrossRoomRent - SupportCharge). The support charge is fixed
    /// in the DB and never grossed up; the tax on it and the security deposit are not grossed up either.
    /// WhtAmount = GrossRoomRent - RoomRentNet. Rounding is half away from zero, the same as SQL Server ROUND.
    /// </summary>
    public static class WhtCalculator
    {
        public sealed record Result(
            decimal RoomRentNet,
            decimal GrossRoomRent,
            decimal RentLine,
            decimal SupportLine,
            decimal TaxLine,
            decimal WhtAmount,
            decimal ArDebit);

        /// <param name="rentExclSupport">Room rent without the support charge (RoomRentExclTax before the gross-up).</param>
        /// <param name="supportCharge">Support / service charges from the DB (not grossed up).</param>
        /// <param name="tax">Existing tax on the support charge (not grossed up).</param>
        /// <param name="whtRatePercent">WHT rate, greater than 0 and less than 100.</param>
        public static Result Calculate(decimal rentExclSupport, decimal supportCharge, decimal tax, decimal whtRatePercent)
        {
            if (whtRatePercent <= 0m || whtRatePercent >= 100m)
                throw new ArgumentOutOfRangeException(nameof(whtRatePercent), "WHT rate must be greater than 0 and less than 100.");

            decimal roomRentNet = rentExclSupport + supportCharge;
            decimal grossRoomRent = Math.Round(roomRentNet / (1m - whtRatePercent / 100m), 2, MidpointRounding.AwayFromZero);
            return new Result(
                RoomRentNet: roomRentNet,
                GrossRoomRent: grossRoomRent,
                RentLine: grossRoomRent - supportCharge,
                SupportLine: supportCharge,
                TaxLine: tax,
                WhtAmount: grossRoomRent - roomRentNet,
                ArDebit: roomRentNet + tax);
        }
    }
}
