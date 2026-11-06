using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkNest.API.Configurations;
using WorkNest.Application.Services;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// The approver's page, no sign-in: read a request by its e-mailed link and approve or reject it. The token travels in the
    /// POST body only, never in the URL, so it never reaches the request log. Reading changes nothing (mail scanners open
    /// links); one database update decides, so a link works once. Rate limited by its own policy "approval-link" (30 per 10 minutes per IP).
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/approvals/public")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class ApprovalPublicController : ControllerBase
    {
        private readonly ApprovalService _service;

        public ApprovalPublicController(ApprovalService service) => _service = service;

        public sealed class ViewRequest { public string? Token { get; set; } }
        public sealed class DecideRequest { public string? Token { get; set; } public string? Decision { get; set; } public string? Note { get; set; } }

        /// <summary>{ token } -> the request. 404 for an unknown link.</summary>
        [EnableRateLimiting(RateLimitingConfig.ApprovalLinkPolicy)]
        [HttpPost("view")]
        public async Task<IActionResult> View([FromBody] ViewRequest? request, CancellationToken ct = default) =>
            ApprovalReply.From(this, await _service.GetPublicAsync(request?.Token, ct), v => v);

        /// <summary>{ token, decision: "Approved" | "Rejected", note } -> the request. 409 for a used or expired link.</summary>
        [EnableRateLimiting(RateLimitingConfig.ApprovalLinkPolicy)]
        [HttpPost("decide")]
        public async Task<IActionResult> Decide([FromBody] DecideRequest? request, CancellationToken ct = default) =>
            ApprovalReply.From(this, await _service.DecideAsync(request?.Token, request?.Decision, request?.Note, ct), v => v, "Thank you. Your decision was saved.");
    }
}
