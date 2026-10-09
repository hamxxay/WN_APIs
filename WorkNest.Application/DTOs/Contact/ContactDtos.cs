namespace WorkNest.Application.DTOs.Contact
{
    public class ContactRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Message { get; set; }
        public string? Phone { get; set; }
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
