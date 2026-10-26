namespace WorkNest.Application.DTOs.Contact
{
    public class ContactRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Message { get; set; }
        public string? Phone { get; set; }
        /// <summary>Staff adding an inquiry only: WalkIn (default) or Reference. Ignored for website / app forms (always Website).</summary>
        public string? InquiryType { get; set; }
        /// <summary>Type Reference: who referred the customer. Empty = the staff member adding it.</summary>
        public string? ReferenceName { get; set; }
        /// <summary>
        /// The branch (dbo.WN_Locations.Id) the inquiry is for. Website / app / customer: optional, must be an active
        /// location. Location-bound staff: one of their own locations (one location: always that one, even when empty).
        /// </summary>
        public int? LocationId { get; set; }
    }

    /// <summary>Set / change an inquiry's branch (Tour Inquiries page, staff). Null = no branch (not for location-bound staff).</summary>
    public class ContactLocationRequest
    {
        public int? LocationId { get; set; }
    }

    /// <summary>Change an inquiry's Type (Tour Inquiries page, staff).</summary>
    public class ContactInquiryTypeRequest
    {
        /// <summary>Website | WhatsApp | WalkIn | Reference</summary>
        public string InquiryType { get; set; } = string.Empty;
        public string? ReferenceName { get; set; }
    }

    /// <summary>
    /// dbo.WN_Contacts.InquiryCategory (INQ_01): "tour" = Tour Inquiries (every inquiry before INQ_01), "general" = Simple
    /// Inquiries (WhatsApp customers who typed free text instead of using the bot menu).
    /// </summary>
    public static class InquiryCategories
    {
        public const string Tour = "tour", General = "general";

        /// <summary>"tour" / "general" for any common spelling ("Tour", "simple", "General"); null when empty or unknown.</summary>
        public static string? Normalize(string? value)
        {
            var v = new string((value ?? "").Where(char.IsLetter).ToArray()).ToLowerInvariant();
            return v switch
            {
                "tour" or "tours" or "tourinquiry" or "tourinquiries" => Tour,
                "general" or "simple" or "simpleinquiry" or "simpleinquiries" => General,
                _ => null
            };
        }
    }

    /// <summary>Where a tour inquiry came from (dbo.WN_Contacts.InquiryType).</summary>
    public static class InquiryTypes
    {
        public const string Website = "Website", WhatsApp = "WhatsApp", WalkIn = "WalkIn", Reference = "Reference";

        /// <summary>The stored value for any common spelling ("walk-in", "Walk In", "whatsapp"...); null when unknown.</summary>
        public static string? Normalize(string? value)
        {
            var v = new string((value ?? "").Where(char.IsLetter).ToArray()).ToLowerInvariant();
            return v switch
            {
                "website" or "web" => Website,
                "whatsapp" => WhatsApp,
                "walkin" => WalkIn,
                "reference" or "referral" => Reference,
                _ => null
            };
        }
    }

    /// <summary>Staff feedback on a tour inquiry (Tour Inquiries page).</summary>
    public class ContactFeedbackRequest
    {
        /// <summary>not_interested | future_prospect | converted</summary>
        public string Outcome { get; set; } = string.Empty;
        /// <summary>Not interested: the reason (required). Otherwise an optional note.</summary>
        public string? Reason { get; set; }
        /// <summary>Future prospect: the date to be alerted on (today or later).</summary>
        public DateTime? FollowUpOn { get; set; }
        /// <summary>Converted: the quotation created from the inquiry.</summary>
        public int? QuotationId { get; set; }
    }

    public class ContactStatusUpdateRequest
    {
        public byte StatusId { get; set; }
    }

    public class ContactDto
    {
        public int? Id { get; set; }
        public string? PublicId { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Message { get; set; }
        public string? ContactType { get; set; }
        public string? CreatedOn { get; set; }
    }
}
