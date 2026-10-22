using WorkNest.API.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.EmailOutbox;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Pending emails (CMS Administration > Services), super admin only: the e-mails asked for while the Email Sending
    /// switch was off (dbo.WN_EmailOutbox, script SS_02_email_sending). They go out within a minute of the switch being
    /// turned on (EmailOutboxService); Discard stops one, or all, for good. Before SS_02 every call answers 503.
    /// </summary>
    [ApiController]
    [Route("api/admin/email-outbox")]
    [Authorize(Roles = ServiceSwitchController.SuperAdminRoles)]
    public class EmailOutboxController : ControllerBase
    {
        private static readonly HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase) { "Pending", "Sending", "Sent", "Failed", "Discarded" };

        private readonly IEmailOutbox _outbox;
        private readonly IServiceSwitch _switches;
        private readonly IEmailService _email;

        public EmailOutboxController(IEmailOutbox outbox, IServiceSwitch switches, IEmailService email)
        {
            _outbox = outbox;
            _switches = switches;
            _email = email;
        }

        /// <summary>?status=Pending|Sending|Sent|Failed|Discarded (none = all): { counts, items, emailSendingOn }, newest first.</summary>
        [HttpGet("")]
        public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int top = 200, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(status) && !Statuses.Contains(status.Trim()))
                return BadRequest(ApiResponse.Fail("status must be Pending, Sending, Sent, Failed or Discarded."));
            try
            {
                var (counts, items) = await _outbox.GetListAsync(string.IsNullOrWhiteSpace(status) ? null : status.Trim(), Math.Clamp(top, 1, 500), ct);
                var on = await _switches.IsEnabledAsync(BackgroundServiceCatalog.EmailSending, ct);
                return Ok(ApiResponse.Ok(new EmailOutboxList { Counts = counts, Items = items, EmailSendingOn = on }));
            }
            catch (EmailOutboxMissingException) { return DatabaseUpdateNeeded(); }
        }

        /// <summary>Sends one Pending / Failed e-mail now. Only while Email Sending is on (off = nothing is e-mailed).</summary>
        [HttpPost("{id:int}/send")]
        public async Task<IActionResult> Send(int id, CancellationToken ct)
        {
            try
            {
                if (!await _switches.IsEnabledAsync(BackgroundServiceCatalog.EmailSending, ct))
                    return Conflict(ApiResponse.Fail("Email Sending is off. Turn it on first; pending e-mails then go out on their own."));
                var claimed = (await _outbox.ClaimAsync(id, 1, ct)).FirstOrDefault();
                if (claimed == null)
                    return NotFound(ApiResponse.Fail("This e-mail is not pending any more (already sent, being sent or discarded)."));
                try
                {
                    await _email.SendHeldAsync(claimed.MessageJson);
                }
                catch (Exception ex)
                {
                    await _outbox.FinishAsync(id, false, ex.Message, CancellationToken.None);
                    return StatusCode(502, ApiResponse.Fail($"The e-mail could not be sent: {ex.Message}"));
                }
                await _outbox.FinishAsync(id, true, null, CancellationToken.None);
                return Ok(ApiResponse.Ok($"E-mail sent to {claimed.ToEmail}."));
            }
            catch (EmailOutboxMissingException) { return DatabaseUpdateNeeded(); }
        }

        /// <summary>Stops one Pending / Failed e-mail for good: it is never sent.</summary>
        [HttpPost("{id:int}/discard")]
        public async Task<IActionResult> Discard(int id, CancellationToken ct)
        {
            try
            {
                var n = await _outbox.DiscardAsync(id, User.GetEmail() ?? User.Identity?.Name, ct);
                return n == 0
                    ? NotFound(ApiResponse.Fail("This e-mail is not pending any more (already sent, being sent or discarded)."))
                    : Ok(ApiResponse.Ok("E-mail discarded: it will not be sent."));
            }
            catch (EmailOutboxMissingException) { return DatabaseUpdateNeeded(); }
        }

        /// <summary>Stops every Pending / Failed e-mail for good.</summary>
        [HttpPost("discard-all")]
        public async Task<IActionResult> DiscardAll(CancellationToken ct)
        {
            try
            {
                var n = await _outbox.DiscardAsync(null, User.GetEmail() ?? User.Identity?.Name, ct);
                return Ok(ApiResponse.Ok(n, n == 1 ? "1 e-mail discarded." : $"{n} e-mails discarded."));
            }
            catch (EmailOutboxMissingException) { return DatabaseUpdateNeeded(); }
        }

        private ObjectResult DatabaseUpdateNeeded() => StatusCode(503, ApiResponse.Fail(EmailOutboxMissingException.Text));
    }
}
