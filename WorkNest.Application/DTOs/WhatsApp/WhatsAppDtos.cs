namespace WorkNest.Application.DTOs.WhatsApp
{
    /// <summary>A conversation in the WhatsApp Inbox (one per customer number).</summary>
    public class WhatsAppConversationDto
    {
        public int Id { get; set; }
        public int ContactId { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Tags { get; set; }
        public string? AdminNotes { get; set; }
        public string Status { get; set; } = "open";
        public DateTime? LastMessageAt { get; set; }
        public string? LastMessageText { get; set; }
        public DateTime? LastIncomingAt { get; set; }
        public int UnreadCount { get; set; }
        /// <summary>True while WhatsApp's 24-hour window is open (a plain reply is allowed; otherwise a template is used).</summary>
        public bool InReplyWindow { get; set; }
    }

    public class WhatsAppMessageDto
    {
        public long Id { get; set; }
        public int ConversationId { get; set; }
        public string Direction { get; set; } = "incoming";
        public string MessageType { get; set; } = "text";
        public string? MessageText { get; set; }
        public int? SentByUserId { get; set; }
        public DateTime CreatedOn { get; set; }
    }

    public class WhatsAppSendMessageRequest
    {
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>Message a number that may not have written yet (starts the conversation).</summary>
    public class WhatsAppStartConversationRequest
    {
        public string Phone { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class WhatsAppContactMetaRequest
    {
        public string? Name { get; set; }
        public string? Tags { get; set; }
        public string? AdminNotes { get; set; }
    }

    public class WhatsAppBroadcastRequest
    {
        public string Message { get; set; } = string.Empty;
        /// <summary>Only contacts with this tag; empty or "all" = everyone who has messaged.</summary>
        public string? Tag { get; set; }
    }

    /// <summary>Result of sending one WhatsApp message.</summary>
    public record WhatsAppSendResult(bool Ok, string? MessageId, string? Error, int? ErrorCode)
    {
        public static WhatsAppSendResult Fail(string error, int? code = null) => new(false, null, error, code);
    }

    /// <summary>Map pin / contact details of a branch (WhatsApp:Branches in appsettings, matched by WN_Locations.Name).</summary>
    public class WhatsAppBranchOptions
    {
        /// <summary>The branch's dbo.WN_Locations.Id, if known: orders the branch list the bot falls back to when
        /// WN_Locations can't be read (it is never saved as a location id unless WN_Locations confirmed it).</summary>
        public int? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Title { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? Phone { get; set; }
        public string? MapsUrl { get; set; }
    }
    /// <summary>A conversation waiting for the "sales representative" follow-up (dbo.WN_WhatsApp_FollowUp_GetPending, INQ_01).
    /// Times are the database's clock (SYSDATETIME, like LastIncomingAt); <see cref="DbNow"/> is "now" on that clock.</summary>
    public class WhatsAppFollowUpCandidate
    {
        public int ConversationId { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string? Name { get; set; }
        public DateTime PendingSince { get; set; }
        public DateTime? LastIncomingAt { get; set; }
        public DateTime? SentAt { get; set; }
        public DateTime? ClaimedAt { get; set; }
        public int? InquiryId { get; set; }
        public string? Text { get; set; }
        /// <summary>Where the customer is in the bot menu (null = no state saved).</summary>
        public string? BotState { get; set; }
        public DateTime DbNow { get; set; }
    }

    /// <summary>The answer of dbo.WN_WhatsApp_FollowUp_Claim: Claimed = this instance sends the follow-up; the general
    /// inquiry (InquiryId) already exists. Not claimed: Reason (not_quiet, claimed, sent_in_last_24h, bot_in_use ...).</summary>
    public class WhatsAppFollowUpClaim
    {
        public bool Claimed { get; set; }
        public string? Reason { get; set; }
        public int? InquiryId { get; set; }
        public string? InquiryPublicId { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Text { get; set; }
    }
}
