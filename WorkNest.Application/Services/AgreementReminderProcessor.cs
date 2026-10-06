using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Agreement;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Agreement signature reminders. Rule (days on the business clock, Pakistan date):
    /// reminder #1 when the agreement was sent FirstAfterDays+ days ago and none was sent; reminder #2 when sent
    /// SecondAfterDays+ days ago, exactly one was sent and at least (SecondAfterDays - FirstAfterDays) days have passed
    /// since it (so a late #1 is never followed by #2 an hour later); stop at MaxReminders.
    /// Sent reminders are the 'AgreementReminder' rows in WN_QuotationActivities, so the count survives restarts.
    /// Send + record run under a per-quotation sp_getapplock so two API instances cannot both send the same reminder.
    /// </summary>
    public class AgreementReminderProcessor : IAgreementReminderProcessor
    {
        public const string ActivityType = "AgreementReminder";

        private readonly IDbRepository _db;
        private readonly IEmailService _email;
        private readonly IAgreementService _agreements;
        private readonly IBusinessClock _clock;
        private readonly ILogger<AgreementReminderProcessor> _logger;

        public AgreementReminderProcessor(IDbRepository db, IEmailService email, IAgreementService agreements, IBusinessClock clock, ILogger<AgreementReminderProcessor> logger)
        {
            _db = db;
            _email = email;
            _agreements = agreements;
            _clock = clock;
            _logger = logger;
        }

        public async Task<List<AgreementReminderOutcome>> SendDueRemindersAsync(AgreementReminderOptions options, Func<int, bool>? skipAgreement = null, CancellationToken cancellationToken = default)
        {
            var outcomes = new List<AgreementReminderOutcome>();
            if (!options.Enabled || options.MaxReminders <= 0) return outcomes;

            var candidates = await _db.GetAgreementsAwaitingSignatureDbAsync();
            foreach (var row in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int agreementId = GetInt(row, "AgreementId");
                if (agreementId <= 0 || (skipAgreement != null && skipAgreement(agreementId))) continue;

                int? reminderNumber = NextDueReminder(row, options);
                if (reminderNumber == null) continue;

                var outcome = new AgreementReminderOutcome
                {
                    AgreementId = agreementId,
                    QuotationId = GetInt(row, "QuotationId"),
                    QuotationNumber = GetString(row, "QuotationNumber"),
                    Email = GetString(row, "CustomerEmail")?.Trim(),
                    ReminderNumber = reminderNumber.Value
                };
                outcomes.Add(outcome);

                if (string.IsNullOrWhiteSpace(outcome.Email))
                {
                    outcome.Result = "Failed";
                    outcome.Error = "Customer has no email address.";
                    continue;
                }

                try
                {
                    await SendOneAsync(outcome, options);
                }
                catch (Exception ex)
                {
                    outcome.Result = "Failed";
                    outcome.Error = ex.Message;
                }
            }

            return outcomes;
        }

        private async Task SendOneAsync(AgreementReminderOutcome outcome, AgreementReminderOptions options)
        {
            // One instance at a time per quotation; if another instance holds it, it is sending this one now.
            await using var appLock = await _db.TryAcquireAppLockDbAsync($"WN_AgreementReminder_Q{outcome.QuotationId}");
            if (appLock == null)
            {
                outcome.Result = "Locked";
                return;
            }

            // Re-check under the lock: another instance may have just recorded this reminder, or the customer signed.
            var fresh = (await _db.GetAgreementsAwaitingSignatureDbAsync(outcome.AgreementId)).FirstOrDefault();
            if (fresh == null || NextDueReminder(fresh, options) != outcome.ReminderNumber)
            {
                outcome.Result = "NoLongerDue";
                return;
            }

            string quotationNumber = GetString(fresh, "QuotationNumber") ?? $"QTN-{outcome.QuotationId}";
            int version = GetInt(fresh, "Version", 1);
            DateTime sentDate = _clock.FromUtc(GetDateTime(fresh, "SentDate")).Date;

            byte[]? pdf = null;
            if (options.AttachPdf)
            {
                try { pdf = await _agreements.GetAgreementPdfAsync(outcome.AgreementId); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Agreement reminder: could not build the PDF for agreement #{AgreementId}; sending without attachment.", outcome.AgreementId);
                }
            }

            string portalUrl = string.IsNullOrWhiteSpace(options.PortalUrl) ? AgreementReminderOptions.DefaultPortalUrl : options.PortalUrl.Trim();

            // Throws on failure -> nothing is recorded, so the reminder is retried on a later run.
            await _email.SendAgreementReminderEmailAsync(
                outcome.Email!,
                GetString(fresh, "CustomerName") ?? "Valued Customer",
                quotationNumber,
                GetString(fresh, "SpaceName"),
                sentDate,
                outcome.ReminderNumber,
                portalUrl,
                pdf);

            try
            {
                await _db.AddQuotationActivityAsync(outcome.QuotationId, version, ActivityType,
                    $"Agreement reminder #{outcome.ReminderNumber} emailed to {outcome.Email} (agreement #{outcome.AgreementId}, sent {sentDate:dd MMM yyyy}).");
                outcome.Result = "Sent";
            }
            catch (Exception ex)
            {
                // The email went out but was not recorded; the caller holds this agreement back so it is not re-sent at once.
                outcome.Result = "SentNotRecorded";
                outcome.Error = ex.Message;
            }
        }

        /// <summary>The reminder number due today for this agreement row, or null when none is due.</summary>
        private int? NextDueReminder(IDictionary<string, object?> row, AgreementReminderOptions options)
        {
            DateTime? sentRaw = GetNullableDateTime(row, "SentDate");
            if (sentRaw == null) return null;

            int sent = GetInt(row, "ReminderCount");
            if (sent >= options.MaxReminders) return null;

            int first = Math.Max(1, options.FirstAfterDays);
            int second = Math.Max(first + 1, options.SecondAfterDays);
            int gap = second - first;
            int next = sent + 1;
            int threshold = next == 1 ? first : second + (next - 2) * gap;

            DateTime today = _clock.Today;
            // SentDate / CreatedDate are stored as UTC; read as UTC a Pakistan-time value only makes a reminder later, never earlier.
            int daysSinceSent = (today - _clock.FromUtc(sentRaw.Value).Date).Days;
            if (daysSinceSent < threshold) return null;

            if (next > 1)
            {
                DateTime? last = GetNullableDateTime(row, "LastReminderAt");
                if (last.HasValue && (today - _clock.FromUtc(last.Value).Date).Days < gap) return null;
            }

            return next;
        }

        private static string? GetString(IDictionary<string, object?> r, string key)
            => r.TryGetValue(key, out var v) && v != null && v != DBNull.Value ? v.ToString() : null;

        private static int GetInt(IDictionary<string, object?> r, string key, int defaultVal = 0)
            => r.TryGetValue(key, out var v) && v != null && v != DBNull.Value && int.TryParse(v.ToString(), out var i) ? i : defaultVal;

        private static DateTime? GetNullableDateTime(IDictionary<string, object?> r, string key)
        {
            if (!r.TryGetValue(key, out var v) || v == null || v == DBNull.Value) return null;
            if (v is DateTime dt) return dt;
            return DateTime.TryParse(v.ToString(), out var parsed) ? parsed : null;
        }

        private static DateTime GetDateTime(IDictionary<string, object?> r, string key)
            => GetNullableDateTime(r, key) ?? DateTime.UtcNow;
    }
}
