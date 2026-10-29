using System.Net.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Network alerts (CMS Administration > Network Alerts), super admin only: who gets the outage e-mails of each
    /// location, a "Send test email" and the last alerts sent (dbo.WN_NetworkAlert*, script NET_ALERT_01_network_alerts).
    /// Before that script every call answers 503.
    /// </summary>
    [ApiController]
    [Route("api/admin/network-alerts")]
    [Authorize(Roles = ServiceSwitchController.SuperAdminRoles)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class NetworkAlertsController : ControllerBase
    {
        private readonly INetworkAlertRepository _repo;
        private readonly NetworkAlertProcessor _processor;

        public NetworkAlertsController(INetworkAlertRepository repo, NetworkAlertProcessor processor)
        {
            _repo = repo;
            _processor = processor;
        }

        public sealed class AddRecipientRequest { public int LocationId { get; set; } public string? Email { get; set; } }
        public sealed class UpdateRecipientRequest { public bool IsActive { get; set; } }
        public sealed class TestRequest { public int LocationId { get; set; } }

        /// <summary>?locationId= (none = every location): the recipients, location by location.</summary>
        [HttpGet("recipients")]
        public Task<IActionResult> List([FromQuery] int? locationId, CancellationToken ct) => Run(async () =>
            Ok(ApiResponse.Ok(await _repo.GetRecipientsAsync(locationId, ct))));

        [HttpPost("recipients")]
        public Task<IActionResult> Add([FromBody] AddRecipientRequest request, CancellationToken ct) => Run(async () =>
        {
            var email = (request?.Email ?? "").Trim();
            if (request == null || request.LocationId <= 0) return BadRequest(ApiResponse.Fail("Choose a location."));
            if (!IsEmail(email)) return BadRequest(ApiResponse.Fail("Enter a valid e-mail address."));
            try
            {
                var added = await _repo.AddRecipientAsync(request.LocationId, email, User.GetEmail() ?? User.Identity?.Name, ct);
                return Ok(ApiResponse.Ok(added, "Recipient added."));
            }
            catch (NetworkAlertRecipientException ex)
            {
                return ex.Duplicate ? Conflict(ApiResponse.Fail(ex.Message)) : BadRequest(ApiResponse.Fail(ex.Message));
            }
        });

        /// <summary>Turns a recipient on / off.</summary>
        [HttpPut("recipients/{id:int}")]
        public Task<IActionResult> Update(int id, [FromBody] UpdateRecipientRequest request, CancellationToken ct) => Run(async () =>
            await _repo.SetRecipientActiveAsync(id, request?.IsActive ?? false, ct)
                ? Ok(ApiResponse.Ok(id, request!.IsActive ? "Recipient turned on." : "Recipient turned off."))
                : NotFound(ApiResponse.Fail("Recipient not found.")));

        [HttpDelete("recipients/{id:int}")]
        public Task<IActionResult> Remove(int id, CancellationToken ct) => Run(async () =>
            await _repo.DeleteRecipientAsync(id, ct)
                ? Ok(ApiResponse.Ok(id, "Recipient removed."))
                : NotFound(ApiResponse.Fail("Recipient not found.")));

        /// <summary>"Send test email": a test message to the location's active recipients.</summary>
        [HttpPost("test")]
        public Task<IActionResult> Test([FromBody] TestRequest request, CancellationToken ct) => Run(async () =>
        {
            if (request == null || request.LocationId <= 0) return BadRequest(ApiResponse.Fail("Choose a location."));
            try
            {
                var sent = await _processor.SendTestAsync(request.LocationId, ct);
                return Ok(ApiResponse.Ok(sent, $"Test e-mail sent to {string.Join(", ", sent)}."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        });

        /// <summary>The last 50 alert e-mails (newest first).</summary>
        [HttpGet("log")]
        public Task<IActionResult> Log(CancellationToken ct) => Run(async () => Ok(ApiResponse.Ok(await _repo.GetLogAsync(50, ct))));

        private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try { return await action(); }
            catch (NetworkAlertsMissingException) { return StatusCode(503, ApiResponse.Fail(NetworkAlertsMissingException.UserMessage)); }
        }

        private static bool IsEmail(string value)
        {
            if (value.Length is 0 or > 256) return false;
            try { var a = new MailAddress(value); return a.Address == value && value.Contains('.', StringComparison.Ordinal) && !value.Contains(' '); }
            catch (FormatException) { return false; }
        }
    }
}
