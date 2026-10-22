using WorkNest.Application.DTOs.EmailOutbox;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Pending emails (dbo.WN_EmailOutbox, script SS_02_email_sending): the e-mails asked for while the Email Sending switch
    /// was off. They go out when it is on again (EmailOutboxService) unless a super admin discards them. Before SS_02 every
    /// call throws <see cref="EmailOutboxMissingException"/>.
    /// </summary>
    public interface IEmailOutbox
    {
        /// <summary>Holds one e-mail; returns its id.</summary>
        Task<int> AddAsync(string kind, string toEmail, string subject, string messageJson, CancellationToken ct = default);
        /// <summary>Counts per status and the newest <paramref name="top"/> e-mails of <paramref name="status"/> (null = all).</summary>
        Task<(EmailOutboxCounts Counts, List<EmailOutboxItem> Items)> GetListAsync(string? status, int top, CancellationToken ct = default);
        /// <summary><paramref name="id"/> = that Pending / Failed e-mail; null = up to <paramref name="top"/> Pending ones. They become Sending.</summary>
        Task<List<ClaimedEmail>> ClaimAsync(int? id, int top, CancellationToken ct = default);
        /// <summary>After a claim: sent, or back to Pending (Failed after 3 attempts) with the error.</summary>
        Task FinishAsync(int id, bool sent, string? error, CancellationToken ct = default);
        /// <summary>Stops e-mails for good: <paramref name="id"/> = that one, null = every Pending / Failed one. Returns how many.</summary>
        Task<int> DiscardAsync(int? id, string? by, CancellationToken ct = default);
    }

    /// <summary>dbo.WN_EmailOutbox / its procedures are missing: SS_02_email_sending has not run on this database.</summary>
    public sealed class EmailOutboxMissingException : Exception
    {
        public const string Text = "Database update needed: run the script SS_02_email_sending.";
        public EmailOutboxMissingException(Exception inner) : base(Text, inner) { }
    }

    /// <summary>Email Sending is off and the e-mail could not be held (SS_02 not run): nothing was e-mailed or kept.</summary>
    public sealed class EmailSendingOffException : InvalidOperationException
    {
        public EmailSendingOffException(string message) : base(message) { }
    }

    /// <summary>
    /// The e-mail was not sent because Email Sending is off; it is held in Pending emails and goes out when the switch is
    /// on again, unless discarded. Callers must not queue it for another retry (it would go out twice).
    /// </summary>
    public sealed class EmailHeldException : InvalidOperationException
    {
        public const string Text = "Email sending is turned off: this e-mail is held in Pending emails (Administration > Services) and goes out when Email Sending is turned on again, unless it is discarded.";
        public int OutboxId { get; }
        public EmailHeldException(int outboxId) : base(Text) => OutboxId = outboxId;
    }
}
