using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.Quotation;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Quotation approvals (contract of 13 Nov 2026): the sales manager sees the quotations a sales executive sent them with a manual
    /// discount, approves them (optionally editing the discount within the cap; the quotation is then sent to the customer) or sends them
    /// back with a note. Sales managers (only what is routed to them, in their locations) and super admins (everything); this is NOT the
    /// general Approvals (Networking / CCTV). Before the script QAPR_01 has run every call answers 503.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "sales_manager,SalesManager,super_admin,SuperAdmin")]
    [SalesManagerAllowed]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class QuotationApprovalsController : ControllerBase
    {
        private readonly IQuotationApprovalService _approvals;
        private readonly IDbRepository _db;

        public QuotationApprovalsController(IQuotationApprovalService approvals, IDbRepository db)
        {
            _approvals = approvals;
            _db = db;
        }

        /// <summary>?status=pending (default) | all: [ { quotationId, quotationNumber, customerName, company, spaceName, locationName, executiveName, basePrice, discountType, discountValue, discountAmount, finalAmount, capPercent, discountReason, submittedAt, approvalStatus } ].</summary>
        [HttpGet("api/quotation-approvals")]
        public Task<IActionResult> List([FromQuery] string? status = null, CancellationToken ct = default)
        {
            var s = (status ?? "pending").Trim().ToLowerInvariant();
            if (s != "pending" && s != "all")
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse.Fail("status must be pending or all.")));
            return Run(async caller => ApiResponse.Ok(await _approvals.ListAsync(caller, pendingOnly: s == "pending", ct)), ct);
        }

        /// <summary>The same row plus history[].</summary>
        [HttpGet("api/quotation-approvals/{quotationId:int}")]
        public Task<IActionResult> Get(int quotationId, CancellationToken ct = default) =>
            Run(async caller => ApiResponse.Ok(await _approvals.GetAsync(caller, quotationId, ct)), ct);

        /// <summary>Body { discountType?: "Percentage" | "Amount", discountValue?, note? }: approves and sends the quotation to the customer.</summary>
        [HttpPost("api/quotation-approvals/{quotationId:int}/approve")]
        public Task<IActionResult> Approve(int quotationId, [FromBody] ApproveQuotationRequest? request, CancellationToken ct = default) =>
            Run(async caller =>
            {
                var (message, row) = await _approvals.ApproveAsync(caller, quotationId, request ?? new ApproveQuotationRequest(), ct);
                return ApiResponse.Ok(row, message);
            }, ct);

        /// <summary>Body { note } (required): the executive edits (or not) and sends again.</summary>
        [HttpPost("api/quotation-approvals/{quotationId:int}/send-back")]
        public Task<IActionResult> SendBack(int quotationId, [FromBody] SendBackQuotationRequest? request, CancellationToken ct = default) =>
            Run(async caller => ApiResponse.Ok(await _approvals.SendBackAsync(caller, quotationId, request ?? new SendBackQuotationRequest(), ct), "Sent back to the sales executive."), ct);

        private static IActionResult Status(int code, string message) => new ObjectResult(ApiResponse.Fail(message)) { StatusCode = code };

        /// <summary>The caller: a sales manager covers their own locations; a super admin everything.</summary>
        private async Task<(QuotationApprovalCaller? Caller, IActionResult? Refusal)> ResolveCallerAsync()
        {
            var email = User.GetEmail();
            var row = string.IsNullOrWhiteSpace(email) ? null : await _db.GetUserByEmailAsync(email);
            if (row == null || !row.TryGetValue("Id", out var id) || id == null)
                return (null, Status(StatusCodes.Status403Forbidden, "The signed-in account could not be verified."));
            int userId = Convert.ToInt32(id);
            if (User.IsSalesManager())
            {
                var mine = User.GetLocationIds();
                if (mine.Count == 0)
                    return (null, Status(StatusCodes.Status403Forbidden, "Your account has no location yet. Ask a Super Admin to assign your locations."));
                return (new QuotationApprovalCaller(userId, false, mine), null);
            }
            return (new QuotationApprovalCaller(userId, true, null), null);
        }

        private async Task<IActionResult> Run(Func<QuotationApprovalCaller, Task<ApiResponse>> action, CancellationToken ct)
        {
            var (caller, refusal) = await ResolveCallerAsync();
            if (caller == null) return refusal!;
            try
            {
                return Ok(await action(caller));
            }
            catch (QuotationApprovalNotSetUpException ex) { return Status(StatusCodes.Status503ServiceUnavailable, ex.Message); }
            catch (QuotationApprovalNotFoundException ex) { return NotFound(ApiResponse.Fail(ex.Message)); }
            catch (QuotationApprovalForbiddenException ex) { return Status(StatusCodes.Status403Forbidden, ex.Message); }
            catch (QuotationApprovalRuleException ex) { return BadRequest(ApiResponse.Fail(ex.Message)); }
            catch (ArgumentException ex) { return BadRequest(ApiResponse.Fail(ex.Message)); }
            catch (InvalidOperationException ex) { return BadRequest(ApiResponse.Fail(ex.Message)); }
        }
    }
}
