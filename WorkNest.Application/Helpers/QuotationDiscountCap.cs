using System;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// The manual discount limit of a quotation, shared by create and by the manager's approve-with-edit. The offering type's cap protects a
    /// floor price per seat: standard rate minus the cap (35,000 - 10% = 31,500). When the base price is raised above the standard rate the
    /// cap widens so the discounted price can come down to that same floor, never below it. Meeting rooms keep the plain cap.
    /// </summary>
    public static class QuotationDiscountCap
    {
        public sealed record Limits(decimal MaxPercent, decimal MaxFixed);

        public static Limits Compute(decimal offeringCapPercent, bool isMeetingRoom, decimal standardMonthlyPerSeat, decimal perSeatBasePrice, int capacity, decimal monthlyBasePrice)
        {
            decimal maxPercent = offeringCapPercent;
            bool raised = !isMeetingRoom && standardMonthlyPerSeat > 0 && perSeatBasePrice > standardMonthlyPerSeat;
            if (raised)
            {
                decimal floorPerSeat = standardMonthlyPerSeat * (1 - offeringCapPercent / 100m);
                maxPercent = Math.Round((perSeatBasePrice - floorPerSeat) / perSeatBasePrice * 100m, 2, MidpointRounding.ToZero);
            }
            decimal maxFixed = Math.Round(monthlyBasePrice * (maxPercent / 100m), 2);
            // Raised base price: allow exactly (base price - floor) per seat, not the rounded percentage.
            if (raised)
            {
                decimal floorPerSeat = standardMonthlyPerSeat * (1 - offeringCapPercent / 100m);
                maxFixed = Math.Round((perSeatBasePrice - floorPerSeat) * capacity, 2);
            }
            return new Limits(maxPercent, maxFixed);
        }
    }
}
