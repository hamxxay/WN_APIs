namespace WorkNest.Application.DTOs.Complaint
{
    /// <summary>Complaint statuses (dbo.WN_Complaints.Status).</summary>
    public static class ComplaintStatus
    {
        public const string Open = "open", InProgress = "in_progress", Resolved = "resolved", Closed = "closed";
        public static readonly string[] All = { Open, InProgress, Resolved, Closed };
    }

    /// <summary>A row of dbo.WN_Complaints.</summary>
    public class ComplaintDto
    {
        public int Id { get; set; }
        public string ComplaintNo { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string? CustomerName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Branch { get; set; }
        /// <summary>The location whose name is <see cref="Branch"/> (case and spaces ignored); null = no branch or no such location.</summary>
        public int? LocationId { get; set; }
        /// <summary>That location's name as stored in dbo.WN_Locations.</summary>
        public string? LocationName { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = ComplaintStatus.Open;
        public bool IsFollowUp { get; set; }
        public int? PreviousComplaintId { get; set; }
        public string? PreviousComplaintNo { get; set; }
        public bool ResolvedNotificationSent { get; set; }
        public DateTime? ResolvedNotificationSentAt { get; set; }
        public string? ResolutionResponse { get; set; }
        public DateTime? ResolutionResponseAt { get; set; }
        public string? StaffNotes { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime? UpdatedOn { get; set; }
    }

    /// <summary>Complaint entered by staff on the Complaints page.</summary>
    public class ComplaintCreateRequest
    {
        public string? CustomerName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        /// <summary>Older forms: the branch name. Used only when <see cref="LocationId"/> is empty.</summary>
        public string? Branch { get; set; }
        /// <summary>
        /// The branch (dbo.WN_Locations.Id), saved as its name in WN_Complaints.Branch. Location-bound staff: one of their
        /// own locations (one location: always that one). Others: an active location, or empty = no branch.
        /// </summary>
        public int? LocationId { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>Set / change a complaint's branch. Null = no branch (not for location-bound staff).</summary>
    public class ComplaintBranchUpdateRequest
    {
        public int? LocationId { get; set; }
    }

    public class ComplaintStatusUpdateRequest
    {
        /// <summary>open | in_progress | resolved | closed</summary>
        public string Status { get; set; } = string.Empty;
        public string? StaffNotes { get; set; }
    }

    // ---- WhatsApp chatbot (machine caller, X-Chatbot-Api-Key) ----

    /// <summary>Book a Tour finished on WhatsApp: saved as a Tour Inquiry.</summary>
    public class ChatbotTourInquiryRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Branch { get; set; }
        /// <summary>The branch's dbo.WN_Locations.Id, saved as WN_Contacts.LocationId (BR_01); the name stays in the message.</summary>
        public int? LocationId { get; set; }
        public string? WorkspaceType { get; set; }
        public int? Seats { get; set; }
        /// <summary>The bot's own booking reference (WN-XXXXXXXX), kept in the message.</summary>
        public string? Reference { get; set; }
    }

    /// <summary>Complaint finished on WhatsApp.</summary>
    public class ChatbotComplaintRequest
    {
        public string Phone { get; set; } = string.Empty;
        public string? Name { get; set; }
        /// <summary>The branch the customer picked in the bot (a dbo.WN_Locations name), saved as WN_Complaints.Branch.</summary>
        public string? Branch { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>The customer's reply to "your complaint has been resolved": 1 = the issue persists, 2 = resolved.</summary>
    public class ChatbotResolutionReplyRequest
    {
        public string Phone { get; set; } = string.Empty;
        /// <summary>persists | resolved</summary>
        public string Response { get; set; } = string.Empty;
    }
}
