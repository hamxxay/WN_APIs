using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkNest.API.Extensions;
using WorkNest.Application.InvoiceContent;
using WorkNest.Common.Responses;
using WorkNest.Infrastructure.ExternalServices.Pdf;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Location-specific invoice content (payment instructions, terms, table headings) printed on the invoice PDFs. Admin and super admin;
    /// an admin only for the locations assigned to them. Versioned like the lease templates: saving keeps ONE draft per location, publishing turns
    /// it into a new immutable version, invoices keep the version they were issued with. Script INVC_01_location_invoice_content; before it every call answers 503.
    /// </summary>
    [ApiController]
    [Route("api/admin/invoice-content")]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class AdminInvoiceContentController : ControllerBase
    {
        private readonly InvoiceContentService _service;
        private readonly ILogger<AdminInvoiceContentController> _logger;

        public AdminInvoiceContentController(InvoiceContentService service, ILogger<AdminInvoiceContentController> logger)
        {
            _service = service;
            _logger = logger;
        }

        public sealed class InvoiceContentRequest { public JsonElement? Content { get; set; } }

        /// <summary>The fields of the form (the CMS renders from this, it hardcodes none).</summary>
        [HttpGet("schema")]
        public IActionResult Schema() => Ok(ApiResponse.Ok(InvoiceContentSchema.ToApi()));

        /// <summary>The locations the caller may manage, with the published version and whether a draft / placeholders exist.</summary>
        [HttpGet("locations")]
        public Task<IActionResult> Locations(CancellationToken ct = default) => Run(async () =>
            Ok(ApiResponse.Ok(await _service.GetLocationsAsync(AllowedLocations(), ct))));

        [HttpGet("{locationId:int}")]
        public Task<IActionResult> Get(int locationId, CancellationToken ct = default) => Run(async () =>
            DenyLocation(locationId) ?? Ok(ApiResponse.Ok(await _service.GetAsync(locationId, ct))));

        [HttpPut("{locationId:int}/draft")]
        public Task<IActionResult> SaveDraft(int locationId, [FromBody] InvoiceContentRequest? request, CancellationToken ct = default) => Run(async () =>
            DenyLocation(locationId) ?? Ok(ApiResponse.Ok(await _service.SaveDraftAsync(locationId, request?.Content, Actor(), ct), "Draft saved.")));

        [HttpDelete("{locationId:int}/draft")]
        public Task<IActionResult> DiscardDraft(int locationId, CancellationToken ct = default) => Run(async () =>
            DenyLocation(locationId) ?? Ok(ApiResponse.Ok(await _service.DiscardDraftAsync(locationId, ct), "Draft discarded.")));

        [HttpPost("{locationId:int}/publish")]
        public Task<IActionResult> Publish(int locationId, CancellationToken ct = default) => Run(async () =>
        {
            var denied = DenyLocation(locationId);
            if (denied != null) return denied;
            var view = await _service.PublishAsync(locationId, Actor(), ct);
            _logger.LogInformation("Invoice content of location {LocationId} published as version {VersionNo} by {Actor}.", locationId, view.Published?.VersionNo, Actor());
            return Ok(ApiResponse.Ok(view, $"Published as version {view.Published?.VersionNo}. New invoices use it; invoices already issued keep their version."));
        });

        public sealed class OneBillSwitchRequest { public bool? Enabled { get; set; } }

        /// <summary>
        /// Super admin only: switch "1Bill on invoices" on / off for a location (script OB_08). Off = no 1Bill line and no new 1Bill number for the
        /// location's invoices; an invoice that already has a number keeps its "with number" line. 400 without enabled, 404 unknown location,
        /// 503 before OB_08. Answers the location like GET.
        /// </summary>
        [HttpPut("{locationId:int}/onebill")]
        [Authorize(Roles = "super_admin,SuperAdmin")]
        public Task<IActionResult> SetOneBill(int locationId, [FromBody] OneBillSwitchRequest? request, CancellationToken ct = default) => Run(async () =>
        {
            var view = await _service.SetOneBillEnabledAsync(locationId, request?.Enabled, Actor(), ct);
            _logger.LogInformation("1Bill on invoices of location {LocationId} switched {State} by {Actor}.", locationId, view.OneBillEnabled ? "ON" : "OFF", Actor());
            return Ok(ApiResponse.Ok(view, view.OneBillEnabled ? "1Bill is on for this location." : "1Bill is off for this location."));
        });

        /// <summary>
        /// The current invoice renderer with sample invoice data: the submitted content, else the draft, else the published version.
        /// Optional query flags (sample data only): withOneBillNumber (default true), withDeposit (default true). Saves and sends nothing.
        /// </summary>
        [HttpPost("{locationId:int}/preview")]
        [EnableRateLimiting("pdf")]
        public Task<IActionResult> Preview(int locationId, [FromBody] InvoiceContentRequest? request, [FromQuery] bool withOneBillNumber = true, [FromQuery] bool withDeposit = true, CancellationToken ct = default) => Run(async () =>
        {
            var denied = DenyLocation(locationId);
            if (denied != null) return denied;
            var content = await _service.GetPreviewContentAsync(locationId, request?.Content, ct);
            // The preview honours the location's 1Bill switch: off = no 1Bill line at all (the sample is a "no number" invoice then).
            var sw = await _service.GetOneBillSwitchAsync(locationId, ct);
            bool oneBillOn = !sw.Available || sw.Enabled;
            content = content.WithOneBillSwitch(oneBillOn);
            var pdf = InvoiceContentPreview.Render(locationId, content, withOneBillNumber && oneBillOn, withDeposit, await _service.GetLocationAddressAsync(locationId, ct));
            Response.Headers["Content-Disposition"] = $"inline; filename=\"Invoice-content-preview-location-{locationId}.pdf\"";
            return File(pdf, "application/pdf");
        });

        /// <summary>Super admin: every location (null). An admin: only their own (the same rule as the other location-scoped pages).</summary>
        private List<int>? AllowedLocations() => User.IsLocationBoundRole() ? User.GetLocationIds() : null;

        private IActionResult? DenyLocation(int locationId)
        {
            var allowed = AllowedLocations();
            if (allowed != null && !allowed.Contains(locationId))
                return StatusCode(403, ApiResponse.Fail($"Location mismatch. You aren't assigned to Location {locationId}."));
            return null;
        }

        private string? Actor() => User.GetEmail() ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("id")?.Value;

        private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try { return await action(); }
            catch (InvoiceContentMissingException) { return StatusCode(503, ApiResponse.Fail(InvoiceContentMissingException.UserMessage)); }
            catch (OneBillSwitchUnavailableException ex) { return StatusCode(503, ApiResponse.Fail(ex.Message)); }
            catch (InvoiceContentLocationNotFoundException ex) { return NotFound(ApiResponse.Fail(ex.Message)); }
            catch (InvoiceContentValidationException ex) { return BadRequest(ApiResponse.Fail(ex.Message, ex.Errors)); }
            catch (InvoiceContentConflictException ex) { return Conflict(ApiResponse.Fail(ex.Message)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invoice content request failed.");
                return StatusCode(500, ApiResponse.Fail("The invoice content request could not be completed. Please try again."));
            }
        }
    }
}
