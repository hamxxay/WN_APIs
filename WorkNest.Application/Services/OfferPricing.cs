using System;
using WorkNest.Application.DTOs.Offers;

namespace WorkNest.Application.Services
{
    /// <summary>The offer maths for one full month of a quotation / booking (see <see cref="OfferPricing.Breakdown"/>).</summary>
    public sealed record OfferBreakdown(
        decimal MonthlyRent, decimal RoomRentPerMonth, decimal OfferDiscountAmount, decimal ManualDiscountAmount,
        decimal FinalMonthlyRent, decimal FinalSecurityDeposit);

    /// <summary>
    /// Offers (WN_Offers): pure rules, no I/O. An offer discounts ROOM RENT ONLY (the monthly rent less the service
    /// charge, which is never discounted). PERCENT = ROUND(room rent x value / 100, 2); FIXED = MIN(value, room rent) per
    /// month of rent, a part month discounted in proportion. The manual discount stays as it is today; offer and manual
    /// are added (not compounded) and never take the room rent below 0: the MANUAL part is reduced first, then the offer.
    /// </summary>
    public static class OfferPricing
    {
        public const string Percent = "PERCENT";
        public const string Fixed = "FIXED";
        public const string FirstPeriod = "FIRST_PERIOD";
        public const string WholeTerm = "WHOLE_TERM";
        public const decimal DefaultPerSeatSupportRate = 2000.00m;

        private static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        public static bool IsPercent(string? type) => string.Equals(type?.Trim(), Percent, StringComparison.OrdinalIgnoreCase);
        public static bool IsFixed(string? type) => string.Equals(type?.Trim(), Fixed, StringComparison.OrdinalIgnoreCase);

        /// <summary>Monthly rent less the service charge (per-seat rate x seats): the part an offer may discount; never below 0.</summary>
        public static decimal RoomRentPerMonth(decimal monthlyRent, int seats, decimal perSeatSupportRate) =>
            Math.Max(0m, monthlyRent - (perSeatSupportRate > 0m ? perSeatSupportRate : DefaultPerSeatSupportRate) * Math.Max(1, seats));

        /// <summary>The offer's discount on ONE full month: PERCENT ROUND(rr x v / 100, 2); FIXED MIN(v, rr).</summary>
        public static decimal MonthlyOfferDiscount(string? type, decimal value, decimal monthlyRoomRent)
        {
            if (value <= 0m || monthlyRoomRent <= 0m) return 0m;
            if (IsPercent(type)) return Round2(monthlyRoomRent * Math.Min(value, 100m) / 100m);
            if (IsFixed(type)) return Round2(Math.Min(value, monthlyRoomRent));
            return 0m;
        }

        /// <summary>
        /// The offer's discount on an invoice. <paramref name="months"/> = the invoice's rent months as the engine counts
        /// them (a prorated stub fraction + whole months). PERCENT ROUND(rr x months x v / 100, 2); FIXED ROUND(MIN(v, rr) x months, 2).
        /// </summary>
        public static decimal InvoiceOfferDiscount(string? type, decimal value, decimal monthlyRoomRent, decimal months)
        {
            if (value <= 0m || monthlyRoomRent <= 0m || months <= 0m) return 0m;
            if (IsPercent(type)) return Round2(monthlyRoomRent * months * Math.Min(value, 100m) / 100m);
            if (IsFixed(type)) return Round2(Math.Min(value, monthlyRoomRent) * months);
            return 0m;
        }

        /// <summary>
        /// Net room rent never below 0: when manual + offer exceed <paramref name="roomRent"/>, the MANUAL part is reduced
        /// first, then the offer. Returns the discounts to use.
        /// </summary>
        public static (decimal Manual, decimal Offer) ClampDiscounts(decimal roomRent, decimal manual, decimal offer)
        {
            roomRent = Math.Max(0m, roomRent);
            manual = Math.Max(0m, manual);
            offer = Math.Max(0m, offer);
            if (manual + offer <= roomRent) return (manual, offer);
            decimal newManual = Math.Max(0m, roomRent - offer);
            if (newManual > manual) newManual = manual;
            decimal newOffer = Math.Min(offer, roomRent);
            return (newManual, newOffer);
        }

        /// <summary>
        /// Security deposit with the offer: <paramref name="baseDeposit"/> is the deposit as it is without an offer (months x
        /// rent with the manual discount mirror). Not applied to the deposit: it is returned as it is. Applied: PERCENT x (1 - pct/100);
        /// FIXED minus value x deposit months; rounded to 2 decimals and never below 0. A 0-month deposit stays 0.
        /// </summary>
        public static decimal Deposit(decimal baseDeposit, int depositMonths, string? type, decimal value, bool applyToDeposit)
        {
            if (baseDeposit <= 0m) return 0m;   // 0 months (or no deposit) stays 0
            if (!applyToDeposit || value <= 0m) return Round2(baseDeposit);
            decimal result = baseDeposit;
            if (IsPercent(type)) result = baseDeposit * (1m - Math.Min(value, 100m) / 100m);
            else if (IsFixed(type)) result = baseDeposit - value * depositMonths;
            return Round2(Math.Max(0m, result));
        }

        /// <summary>The manual discount on ONE month of rent (the existing rule: a percentage of the monthly rent, or a fixed PKR amount per month, at most the rent).</summary>
        public static decimal ManualMonthlyDiscount(string? manualType, decimal manualValue, decimal monthlyRent)
        {
            if (manualValue <= 0m || monthlyRent <= 0m) return 0m;
            bool fixedType = string.Equals(manualType?.Trim(), "Amount", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(manualType?.Trim(), "Fixed", StringComparison.OrdinalIgnoreCase);
            return fixedType ? Round2(Math.Min(manualValue, monthlyRent)) : Round2(monthlyRent * Math.Min(manualValue, 100m) / 100m);
        }

        /// <summary>
        /// The one-month figures stored on a quotation / booking with an offer: offer and manual discount (manual reduced
        /// first when together they exceed the room rent), final monthly rent and final deposit.
        /// </summary>
        public static OfferBreakdown Breakdown(string? offerType, decimal offerValue, bool applyToDeposit, decimal monthlyRent, int seats,
            decimal perSeatSupportRate, string? manualType, decimal manualValue, int depositMonths, decimal baseDepositWithManualMirror)
        {
            decimal roomRent = RoomRentPerMonth(monthlyRent, seats, perSeatSupportRate);
            decimal offer = MonthlyOfferDiscount(offerType, offerValue, roomRent);
            decimal manual = ManualMonthlyDiscount(manualType, manualValue, monthlyRent);
            (manual, offer) = ClampDiscounts(roomRent, manual, offer);
            decimal finalRent = Math.Max(0m, monthlyRent - manual - offer);
            decimal deposit = Deposit(baseDepositWithManualMirror, depositMonths, offerType, offerValue, applyToDeposit);
            return new OfferBreakdown(monthlyRent, roomRent, offer, manual, finalRent, deposit);
        }

        /// <summary>The snapshot columns for a quotation / booking from an offer and its <see cref="OfferBreakdown"/>.</summary>
        public static OfferSnapshot ToSnapshot(OfferDto offer, OfferBreakdown b) => new()
        {
            OfferId = offer.OfferId,
            OfferName = offer.Name,
            OfferDiscountType = offer.DiscountType,
            OfferDiscountValue = offer.DiscountValue,
            OfferApplyTo = offer.ApplyTo,
            OfferApplyToDeposit = offer.ApplyToDeposit,
            OfferDiscountAmount = b.OfferDiscountAmount,
            ManualDiscountAmount = b.ManualDiscountAmount,
            FinalMonthlyRent = b.FinalMonthlyRent,
            FinalSecurityDeposit = b.FinalSecurityDeposit
        };

        /// <summary>Two active offers overlap in time (back-to-back is allowed: s1 &lt; e2 and s2 &lt; e1) and in place (an all-locations offer meets every location).</summary>
        public static bool Overlaps(DateTime start1, DateTime end1, int? location1, DateTime start2, DateTime end2, int? location2) =>
            start1 < end2 && start2 < end1 && (location1 == null || location2 == null || location1 == location2);

        public static bool Overlaps(OfferDto a, OfferDto b) => Overlaps(a.StartDate, a.EndDate, a.LocationId, b.StartDate, b.EndDate, b.LocationId);

        /// <summary>StartDate &lt;= at &lt; EndDate (strict on EndDate) and the offer is for all locations or this one.</summary>
        public static bool Matches(DateTime start, DateTime end, int? offerLocationId, int? locationId, DateTime at) =>
            start <= at && at < end && (offerLocationId == null || offerLocationId == locationId);

        public static bool Matches(OfferDto offer, int? locationId, DateTime at) =>
            offer.IsActive && Matches(offer.StartDate, offer.EndDate, offer.LocationId, locationId, at);

        /// <summary>
        /// Q7: a quotation past its ValidUntil day (Pakistan date, QuotationValidity) no longer gives its offer to the booking.
        /// </summary>
        public static bool QuotationKeepsOffer(DateTime validUntil, DateTime todayPk) =>
            !Helpers.QuotationValidity.IsPast(validUntil, todayPk);

        /// <summary>Plain-words description of an offer (agreement page 1, PDFs): "15% off room rent" / "PKR 5,000 off room rent per month".</summary>
        public static string Describe(string? type, decimal value, string? applyTo, bool applyToDeposit)
        {
            string what = IsPercent(type) ? $"{value:0.##}% off room rent" : $"PKR {value:N0} off room rent per month";
            string scope = string.Equals(applyTo, WholeTerm, StringComparison.OrdinalIgnoreCase) ? "every invoice of the booking" : "the first invoice";
            return $"{what} on {scope}{(applyToDeposit ? ", also off the security deposit" : "")}";
        }
    }
}
