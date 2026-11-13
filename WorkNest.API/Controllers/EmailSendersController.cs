using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>Request body of PUT api/admin/email-senders/{typeKey}.</summary>
    public class EmailSenderSaveRequest
    {
        public string? Mailbox { get; set; }
        public string? FromName { get; set; }
    }

    /// <summary>Request body of POST api/admin/email-senders/test.</summary>
    public class EmailSenderTestRequest
    {
        public string? Mailbox { get; set; }
        public string? ToEmail { get; set; }
    }

    /// <summary>
    /// Email Senders (CMS Administration), super admin only: which mailbox (sales / notifications) sends each e-mail type.
    /// Passwords stay in server configuration; only addresses and servers are shown. A change applies to the next e-mail.
    /// </summary>
    [ApiController]
    [Route("api/admin/email-senders")]
    [Authorize(Roles = ServiceSwitchController.SuperAdminRoles)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class EmailSendersController : ControllerBase
    {
        private readonly IEmailSenderRepository _repo;
        private readonly IEmailSenderResolver _resolver;
        private readonly IConfiguration _config;

        public EmailSendersController(IEmailSenderRepository repo, IEmailSenderResolver resolver, IConfiguration config)
        { _repo = repo; _resolver = resolver; _config = config; }

        /// <summary>{ mailboxes: [{ key, address, host, port, ready }], items: [...], editable }.</summary>
        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct = default)
        {
            Dictionary<string, EmailSenderRow>? rows = null;
            try { rows = (await _repo.GetAllAsync(ct)).ToDictionary(r => r.TypeKey, StringComparer.OrdinalIgnoreCase); }
            catch (EmailSendersMissingException) { }
            return Ok(ApiResponse.Ok(new { mailboxes = Mailboxes(), items = EmailSenderRules.Items(rows), editable = rows != null }));
        }

        [HttpPut("{typeKey}")]
        public async Task<IActionResult> Save(string typeKey, [FromBody] EmailSenderSaveRequest? request, CancellationToken ct = default)
        {
            var type = EmailTypes.Find(typeKey);
            if (type == null) return NotFound(ApiResponse.Fail("Unknown e-mail type."));
            if (request == null) return BadRequest(ApiResponse.Fail("Request body is required."));
            var mailbox = request.Mailbox?.Trim().ToLowerInvariant();
            var error = EmailSenderRules.Validate(mailbox, request.FromName);
            if (error != null) return BadRequest(ApiResponse.Fail(error));
            try
            {
                var row = await _repo.SaveAsync(type.Key, mailbox!, request.FromName, User.GetEmail(), ct);
                _resolver.Invalidate();
                return Ok(ApiResponse.Ok(EmailSenderRules.ToItem(type, new Dictionary<string, EmailSenderRow> { [type.Key] = row }), "Email sender saved."));
            }
            catch (EmailSendersMissingException) { return StatusCode(503, ApiResponse.Fail(EmailSendersMissingException.UserMessage)); }
        }

        /// <summary>Sends a test e-mail from one mailbox now (never held). 200 "Test e-mail sent ..." or 400 with the mail server's reason.</summary>
        [HttpPost("test")]
        public async Task<IActionResult> Test([FromBody] EmailSenderTestRequest? request, [FromServices] IEmailService email)
        {
            var mailbox = request?.Mailbox?.Trim().ToLowerInvariant();
            if (mailbox != EmailMailboxes.Sales && mailbox != EmailMailboxes.Notifications)
                return BadRequest(ApiResponse.Fail("Choose the sales or notifications mailbox."));
            var to = request?.ToEmail?.Trim();
            if (string.IsNullOrWhiteSpace(to) || !System.Net.Mail.MailAddress.TryCreate(to, out var addr) || addr.Address != to)
                return BadRequest(ApiResponse.Fail("Enter a valid e-mail address to send the test to."));
            try
            {
                var route = await email.SendMailboxTestAsync(mailbox, to, User.GetEmail());
                return Ok(ApiResponse.Ok(new { route }, $"Test e-mail sent to {to} from {route}. Check the inbox (and spam)."));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse.Fail($"The test e-mail was not sent: {ex.GetBaseException().Message}"));
            }
        }

        private object[] Mailboxes()
        {
            var salesPort = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587;
            var salesHost = _config["Email:SmtpHost"] ?? "smtppro.zoho.com";
            var n = EmailMailboxes.NotificationsSettings(_config);
            var nAddress = (n?.FromEmail ?? _config["Email:Notifications:FromEmail"])?.Trim() ?? "";
            var nHost = n?.Host ?? (string.IsNullOrWhiteSpace(_config["Email:Notifications:SmtpHost"]) ? salesHost : _config["Email:Notifications:SmtpHost"]!);
            var nPort = n?.Port ?? (int.TryParse(_config["Email:Notifications:SmtpPort"], out var np) ? np : salesPort);
            var salesReady = !string.IsNullOrWhiteSpace(_config["Email:FromEmail"]) && !string.IsNullOrWhiteSpace(_config["Email:Password"] ?? _config["Email:GmailAppPassword"]);
            return new object[]
            {
                new { key = EmailMailboxes.Sales, address = _config["Email:FromEmail"] ?? "", host = salesHost, port = salesPort, ready = salesReady },
                new { key = EmailMailboxes.Notifications, address = nAddress, host = nHost, port = nPort, ready = n != null },
            };
        }
    }
}
