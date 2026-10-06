namespace WorkNest.Application.DTOs.Agreement
{
    /// <summary>
    /// "AgreementReminders" config section. Reminder #1 goes FirstAfterDays after the agreement was sent,
    /// reminder #2 SecondAfterDays after; at most MaxReminders are sent.
    /// </summary>
    public class AgreementReminderOptions
    {
        public const string DefaultPortalUrl = "https://worknest.vercel.app/my-agreements";

        public bool Enabled { get; set; } = true;
        public int FirstAfterDays { get; set; } = 3;
        public int SecondAfterDays { get; set; } = 7;
        public int MaxReminders { get; set; } = 2;
        /// <summary>Customer portal page where the agreement is downloaded and the signed copy uploaded.</summary>
        public string PortalUrl { get; set; } = DefaultPortalUrl;
        /// <summary>Attach the agreement PDF to the reminder (falls back to no attachment if generation fails).</summary>
        public bool AttachPdf { get; set; } = true;
    }

    /// <summary>What happened to one due agreement in a reminder run.</summary>
    public class AgreementReminderOutcome
    {
        public int AgreementId { get; set; }
        public int QuotationId { get; set; }
        public string? QuotationNumber { get; set; }
        public string? Email { get; set; }
        public int ReminderNumber { get; set; }
        /// <summary>Sent, Failed, SentNotRecorded, Locked (another instance is handling it) or NoLongerDue.</summary>
        public string Result { get; set; } = "";
        public string? Error { get; set; }
    }
}
