namespace WorkNest.Application.DTOs.Challan
{
    /// <summary>Body of POST api/challan/extend-validity (Challan Validity Extension page).</summary>
    public class ChallanExtendValidityRequest
    {
        public int BookingId { get; set; }
        /// <summary>New expiry date (yyyy-MM-dd); must be after the current one.</summary>
        public string NewExpiryDate { get; set; } = string.Empty;
        public string? Remarks { get; set; }
    }
}
