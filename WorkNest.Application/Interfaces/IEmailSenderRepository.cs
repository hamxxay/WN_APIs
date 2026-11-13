using WorkNest.Application.Services;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Reads and changes dbo.WN_EmailSenders (script EMAIL_01): which mailbox sends each e-mail type.</summary>
    public interface IEmailSenderRepository
    {
        /// <summary>Every saved row. Throws <see cref="EmailSendersMissingException"/> before EMAIL_01 runs.</summary>
        Task<IReadOnlyList<EmailSenderRow>> GetAllAsync(CancellationToken ct = default);

        /// <summary>Saves the mailbox (and optional sender name) of a type, inserting the row when it is missing. Returns the saved row.</summary>
        Task<EmailSenderRow> SaveAsync(string typeKey, string mailbox, string? fromName, string? userEmail, CancellationToken ct = default);
    }

    /// <summary>The per-type lookup the e-mail service uses (cached for at most 60 seconds). Never throws: without the table the defaults apply.</summary>
    public interface IEmailSenderResolver
    {
        Task<(string Mailbox, string? FromName)> ResolveAsync(string typeKey, CancellationToken ct = default);
        /// <summary>The saved rows; null when the table does not exist yet.</summary>
        Task<IReadOnlyDictionary<string, EmailSenderRow>?> GetRowsAsync(CancellationToken ct = default);
        /// <summary>Forgets the cache so a saved change applies to the next e-mail.</summary>
        void Invalidate();
    }

    /// <summary>dbo.WN_EmailSenders is missing (SQL errors 208 / 2812): answered as 503.</summary>
    public sealed class EmailSendersMissingException : Exception
    {
        public const string UserMessage = "Email senders are not set up on this server yet.";
        public EmailSendersMissingException(Exception? inner = null) : base(UserMessage, inner) { }
    }
}
