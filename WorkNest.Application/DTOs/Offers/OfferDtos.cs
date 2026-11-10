using System;

namespace WorkNest.Application.DTOs.Offers
{
    /// <summary>A row of dbo.WN_Offers as dbo.WN_Offers_GetList / WN_Offers_GetRunning return it (script OFFER_01_offers).</summary>
    public class OfferDto
    {
        public int OfferId { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>'PERCENT' or 'FIXED' (FIXED = PKR per month of room rent).</summary>
        public string DiscountType { get; set; } = "PERCENT";
        public decimal DiscountValue { get; set; }
        /// <summary>Pakistan wall-clock date-time, Kind Unspecified (written without Z / offset).</summary>
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        /// <summary>'FIRST_PERIOD' (the booking's first invoice only) or 'WHOLE_TERM'.</summary>
        public string ApplyTo { get; set; } = "FIRST_PERIOD";
        public bool ApplyToDeposit { get; set; }
        public bool IsActive { get; set; } = true;
        public int? CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        /// <summary>'Inactive' / 'Upcoming' / 'Running' / 'Ended' (computed by the database). Null from GetRunning.</summary>
        public string? Status { get; set; }
        public int UsedCount { get; set; }
    }

    /// <summary>Body of POST api/admin/offers and PUT api/admin/offers/{id}. Dates are local Pakistan "yyyy-MM-ddTHH:mm" strings.</summary>
    public class OfferSaveRequest
    {
        public string? Name { get; set; }
        public string? DiscountType { get; set; }
        public decimal DiscountValue { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public int? LocationId { get; set; }
        public string? ApplyTo { get; set; } = "FIRST_PERIOD";
        public bool ApplyToDeposit { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// The 10 offer snapshot columns that sit on dbo.WN_Quotations, dbo.WN_Bookings and dbo.WN_Invoices (same names).
    /// All null = no offer. Quotation / booking: amounts are for ONE full month; invoice: amounts on THAT invoice.
    /// </summary>
    public class OfferSnapshot
    {
        public int? OfferId { get; set; }
        public string? OfferName { get; set; }
        public string? OfferDiscountType { get; set; }
        public decimal? OfferDiscountValue { get; set; }
        public string? OfferApplyTo { get; set; }
        public bool? OfferApplyToDeposit { get; set; }
        public decimal? OfferDiscountAmount { get; set; }
        public decimal? ManualDiscountAmount { get; set; }
        public decimal? FinalMonthlyRent { get; set; }
        public decimal? FinalSecurityDeposit { get; set; }

        public bool HasOffer => OfferId.HasValue && OfferId.Value > 0;
    }

    /// <summary>Which table a snapshot is read from / written to.</summary>
    public enum OfferEntity
    {
        Quotation,
        Booking,
        Invoice
    }
}
