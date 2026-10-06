using WorkNest.Application.DTOs.Agreement;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Finds agreements still waiting for the customer's signature and emails the reminders that are due.</summary>
    public interface IAgreementReminderProcessor
    {
        /// <summary>
        /// Sends every reminder due now. <paramref name="skipAgreement"/> lets the caller hold back agreements
        /// (e.g. ones that failed recently). A reminder is recorded as an 'AgreementReminder' quotation activity
        /// only after the email was sent.
        /// </summary>
        Task<List<AgreementReminderOutcome>> SendDueRemindersAsync(AgreementReminderOptions options, Func<int, bool>? skipAgreement = null, CancellationToken cancellationToken = default);
    }
}
