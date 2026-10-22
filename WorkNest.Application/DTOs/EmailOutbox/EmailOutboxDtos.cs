namespace WorkNest.Application.DTOs.EmailOutbox
{
    /// <summary>
    /// A whole e-mail kept while the Email Sending switch is off (dbo.WN_EmailOutbox.MessageJson, script SS_02): what
    /// is needed to send it later exactly as it was asked for.
    /// </summary>
    public sealed class HeldEmail
    {
        public string FromName { get; set; } = "";
        public List<string> To { get; set; } = new();
        public List<string> Cc { get; set; } = new();
        public List<string> Bcc { get; set; } = new();
        public List<string> ReplyTo { get; set; } = new();
        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public bool IsHtml { get; set; }
        public List<HeldAttachment> Attachments { get; set; } = new();
    }

    public sealed class HeldAttachment
    {
        public string FileName { get; set; } = "attachment";
        public string ContentType { get; set; } = "application/octet-stream";
        public string Base64 { get; set; } = "";
    }

    /// <summary>One row of the Pending emails list (without the message itself).</summary>
    public sealed class EmailOutboxItem
    {
        public int Id { get; set; }
        /// <summary>What it is: "Agreement e-mail", "Invoice / challan e-mail", ...</summary>
        public string Kind { get; set; } = "";
        public string ToEmail { get; set; } = "";
        public string Subject { get; set; } = "";
        /// <summary>Pending | Sending | Sent | Failed | Discarded</summary>
        public string Status { get; set; } = "";
        public int Attempts { get; set; }
        public string? LastError { get; set; }
        /// <summary>UTC.</summary>
        public DateTime CreatedOn { get; set; }
        public DateTime? SentOn { get; set; }
        public DateTime? DiscardedOn { get; set; }
        public string? DiscardedBy { get; set; }
    }

    public sealed class EmailOutboxCounts
    {
        public int Pending { get; set; }
        public int Sending { get; set; }
        public int Sent { get; set; }
        public int Failed { get; set; }
        public int Discarded { get; set; }
    }

    public sealed class EmailOutboxList
    {
        public EmailOutboxCounts Counts { get; set; } = new();
        public List<EmailOutboxItem> Items { get; set; } = new();
        /// <summary>The Email Sending switch now: on = Pending e-mails go out within a minute.</summary>
        public bool EmailSendingOn { get; set; }
    }

    /// <summary>A claimed e-mail (Status Sending): send it, then finish it.</summary>
    public sealed class ClaimedEmail
    {
        public int Id { get; set; }
        public string Kind { get; set; } = "";
        public string ToEmail { get; set; } = "";
        public string Subject { get; set; } = "";
        public string MessageJson { get; set; } = "";
        public int Attempts { get; set; }
    }
}
