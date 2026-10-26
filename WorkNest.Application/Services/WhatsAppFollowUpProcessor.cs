using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.WhatsApp;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// One tick of the WhatsApp follow-up (Simple Inquiries, INQ_01). A customer who typed free text at the bot's main
    /// menu (the bot answered "Please select a valid option") and then went quiet for WhatsApp:FollowUpQuietSeconds
    /// (default 120) gets "Our sales representative will contact you shortly." and ONE general inquiry is created with
    /// the text they typed (Tour Inquiries > Simple Inquiries).
    /// Rules: at most one follow-up per conversation in 24 hours; nothing when the bot has been used since (a valid choice
    /// clears the pending state; a conversation in a bot flow is skipped); only inside WhatsApp's 24-hour window.
    /// Idempotent across API instances: dbo.WN_WhatsApp_FollowUp_Claim locks the row and leases it, and creates the
    /// inquiry once; a failed send releases the lease and the next tick retries with the same inquiry.
    /// All times are the database clock (the candidates carry DbNow), so the API and database clocks never disagree.
    /// </summary>
    public class WhatsAppFollowUpProcessor
    {
        public const string FollowUpMessage = "Our sales representative will contact you shortly.";
        public const int DefaultQuietSeconds = 120;
        public const int DefaultLeaseSeconds = 300;
        private static readonly TimeSpan Cap = TimeSpan.FromHours(24);

        private readonly IWhatsAppRepository _repo;
        private readonly IWhatsAppInboxService _sender;
        private readonly ILogger<WhatsAppFollowUpProcessor> _logger;

        public WhatsAppFollowUpProcessor(IWhatsAppRepository repo, IWhatsAppInboxService sender, IConfiguration configuration,
            ILogger<WhatsAppFollowUpProcessor> logger)
        {
            _repo = repo;
            _sender = sender;
            _logger = logger;
            QuietSeconds = PositiveInt(configuration["WhatsApp:FollowUpQuietSeconds"], DefaultQuietSeconds);
            LeaseSeconds = PositiveInt(configuration["WhatsApp:FollowUpLeaseSeconds"], DefaultLeaseSeconds);
        }

        public int QuietSeconds { get; }
        public int LeaseSeconds { get; }

        /// <summary>What one tick did (for the log and the tests).</summary>
        public sealed record TickResult(int Sent, int Failed, int Dropped, int Waiting);

        public async Task<TickResult> RunOnceAsync(CancellationToken ct = default)
        {
            int sent = 0, failed = 0, dropped = 0, waiting = 0;
            var quiet = TimeSpan.FromSeconds(QuietSeconds);
            var lease = TimeSpan.FromSeconds(LeaseSeconds);

            foreach (var c in await _repo.GetFollowUpCandidatesAsync())
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    var now = c.DbNow;

                    // Still typing (or no customer message recorded): wait for the quiet spell.
                    if (c.LastIncomingAt is not { } last || now - last < quiet) { waiting++; continue; }

                    string? drop = null;
                    if (now - last > Cap) drop = "WhatsApp's 24-hour window has closed";
                    else if (c.SentAt is { } sentAt && now - sentAt < Cap) drop = "a follow-up was already sent in the last 24 hours";
                    else if (c.BotState is { } state && state != "main_menu") drop = $"the customer is using the bot ({state})";
                    if (drop != null)
                    {
                        await _repo.ClearFollowUpPendingAsync(c.ConversationId);
                        _logger.LogInformation("WhatsApp follow-up for conversation {ConversationId} dropped: {Reason}.", c.ConversationId, drop);
                        dropped++;
                        continue;
                    }

                    // Another instance is sending it right now.
                    if (c.ClaimedAt is { } claimedAt && now - claimedAt < lease) { waiting++; continue; }

                    var claim = await _repo.ClaimFollowUpAsync(c.ConversationId, QuietSeconds, LeaseSeconds);
                    if (claim is not { Claimed: true }) { waiting++; continue; }

                    bool ok;
                    string? error;
                    try
                    {
                        // The Inbox helper: plain text inside the 24-hour window, else the approved template; records the message.
                        (ok, _, error) = await _sender.SendToPhoneAsync(claim.PhoneNumber, null, FollowUpMessage, null);
                    }
                    catch (Exception ex)
                    {
                        ok = false;
                        error = ex.Message;
                    }

                    if (ok)
                    {
                        await _repo.CompleteFollowUpAsync(c.ConversationId);
                        _logger.LogInformation("WhatsApp follow-up sent to conversation {ConversationId}; simple inquiry {InquiryId} created.",
                            c.ConversationId, claim.InquiryId);
                        sent++;
                    }
                    else
                    {
                        await _repo.ReleaseFollowUpAsync(c.ConversationId);
                        _logger.LogWarning("WhatsApp follow-up to conversation {ConversationId} failed ({Error}); retrying next tick (inquiry {InquiryId} kept).",
                            c.ConversationId, error, claim.InquiryId);
                        failed++;
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    // One conversation's problem (e.g. the database) never stops the others; the next tick retries it.
                    _logger.LogError(ex, "WhatsApp follow-up for conversation {ConversationId} failed.", c.ConversationId);
                    failed++;
                }
            }
            return new TickResult(sent, failed, dropped, waiting);
        }

        private static int PositiveInt(string? value, int fallback) =>
            int.TryParse(value, out var v) && v > 0 ? v : fallback;
    }
}
