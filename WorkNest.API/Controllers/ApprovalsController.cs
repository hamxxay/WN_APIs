using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.Approvals;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Approvals for signed-in staff (CMS Approvals tab, app "Requests"): raise a request, list, open one, send an expired one
    /// again and mark an approved one Done (script APPROVAL_01_approvals; before it every call answers 503).
    /// Sales executives, admins and super admins raise; sales executives and admins see what their locations hold, super admins everything, and everyone (network engineers and access officers only this) what was sent to them.
    /// Network engineers and access officers are refused everywhere else, so this carries their allow attributes; the CEO and
    /// Sales Manager are refused by their own filters (no allow attribute here). Customers are not in the Authorize roles.
    /// </summary>
    [ApiController]
    [Route("api/approvals")]
    [Authorize(Roles = StaffRoles)]
    [NetworkEngineerAllowed]
    [AccessOfficerAllowed]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class ApprovalsController : ControllerBase
    {
        /// <summary>The staff roles of the contract. CEO, Sales Manager and customers (general) are not in it.</summary>
        public const string StaffRoles =
            "super_admin,SuperAdmin,admin,Admin,sales_executive,SalesExecutive,network_engineer,NetworkEngineer,access_officer,AccessOfficer";

        public const string OtherLocationMessage = "Pick one of your own locations for this request.";
        public const string CannotRaiseMessage = "Only sales executives, admins and super admins can raise a request.";

        private readonly ApprovalService _service;
        private readonly ICustomInvoiceAccessService _users;

        /// <param name="users">Only for <c>ResolveUserIdAsync</c>: the token carries the public id (a GUID) and the e-mail, so the WN_Users.Id is looked up by e-mail (cached).</param>
        public ApprovalsController(ApprovalService service, ICustomInvoiceAccessService users)
        {
            _service = service;
            _users = users;
        }

        public sealed class CreateRequest
        {
            public int LocationId { get; set; }
            public int CategoryId { get; set; }
            public string? Title { get; set; }
            public string? Details { get; set; }
            public string? Reason { get; set; }
        }

        public sealed class DoneRequest { public string? Note { get; set; } }

        /// <summary>?status=&amp;categoryId=&amp;page=&amp;pageSize=: { items, totalCount, page, pageSize }.</summary>
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int? categoryId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        {
            var caller = await CallerAsync();
            if (caller == null) return SignInAgain();
            return ApprovalReply.From(this, await _service.ListAsync(caller, status, categoryId, page, pageSize, ct), v => new
            {
                items = v.Items.Select(ListItem).ToList(),
                totalCount = v.TotalCount,
                page = v.Page,
                pageSize = v.PageSize
            });
        }

        /// <summary>{ categories (active), approverEmailSet, canCreate, locations: [{ id, name }] }.</summary>
        [HttpGet("meta")]
        public async Task<IActionResult> Meta(CancellationToken ct = default)
        {
            var caller = await CallerAsync();
            if (caller == null) return SignInAgain();
            return ApprovalReply.From(this, await _service.MetaAsync(caller, ct), v => new
            {
                categories = v.Categories.Select(c => new { id = c.Id, name = c.Name, isActive = c.IsActive }).ToList(),
                approverEmailSet = v.ApproverEmailSet,
                canCreate = v.CanCreate,
                locations = v.Locations.Select(l => new { id = l.Id, name = l.Name }).ToList()
            });
        }

        /// <summary>One request. 403 outside the caller's locations unless it was sent to them.</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct = default)
        {
            var caller = await CallerAsync();
            if (caller == null) return SignInAgain();
            return ApprovalReply.From(this, await _service.GetAsync(id, caller, ct), v => Detail(v));
        }

        /// <summary>Raises a request at one of the caller's locations (a super admin: any). Sales executive, admin or super admin only.</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateRequest? request, CancellationToken ct = default)
        {
            var caller = await CallerAsync();
            if (caller == null) return SignInAgain();
            if (!caller.CanRaise) return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail(CannotRaiseMessage));
            if (request == null) return BadRequest(ApiResponse.Fail("Fill in the request."));
            if (!caller.IsSuperAdmin && !caller.LocationIds.Contains(request.LocationId))
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail(OtherLocationMessage));
            var result = await _service.CreateAsync(new ApprovalCreateInput(request.LocationId, request.CategoryId, request.Title, request.Details, request.Reason), caller.UserId, ct);
            return ApprovalReply.From(this, result, v => new { id = v.Id, emailSent = v.EmailSent }, "Request sent for approval.");
        }

        /// <summary>Sends an expired request again with a new link. The caller must be able to raise at its location.</summary>
        [HttpPost("{id:int}/resend")]
        public async Task<IActionResult> Resend(int id, CancellationToken ct = default)
        {
            var caller = await CallerAsync();
            if (caller == null) return SignInAgain();
            var seen = await _service.GetAsync(id, caller, ct);
            if (!seen.IsOk) return ApprovalReply.From(this, seen, _ => new { });
            if (seen.Data!.Request.Status == "Expired" && !seen.Data.CanResend)
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("You can't send this request again."));
            return ApprovalReply.From(this, await _service.ResendAsync(id, ct), v => new { id = v.Id, emailSent = v.EmailSent }, "Request sent again.");
        }

        /// <summary>Marks an approved request Done. Only the super admin, or a person the request was sent to.</summary>
        [HttpPost("{id:int}/done")]
        public async Task<IActionResult> Done(int id, [FromBody] DoneRequest? body, CancellationToken ct = default)
        {
            var caller = await CallerAsync();
            if (caller == null) return SignInAgain();
            return ApprovalReply.From(this, await _service.MarkDoneAsync(id, caller.UserId, caller.IsSuperAdmin, body?.Note, ct), rid => new { id = rid }, "Marked Done.");
        }

        // ---- shapes of the contract --------------------------------------------------------------------------------

        private static object ListItem(ApprovalRequestRow r) => new
        {
            id = r.Id, locationId = r.LocationId, locationName = r.LocationName, categoryId = r.CategoryId, categoryName = r.CategoryName, title = r.Title,
            status = r.Status, requestedByUserId = r.RequestedByUserId, requestedByName = r.RequestedByName, requestedAt = r.RequestedAt,
            decidedAt = r.DecidedAt, doneAt = r.DoneAt
        };

        /// <summary>Everything of the contract's RequestDetail and no e-mail address.</summary>
        private static object Detail(ApprovalDetailView v)
        {
            var r = v.Request;
            return new
            {
                id = r.Id, locationId = r.LocationId, locationName = r.LocationName, categoryId = r.CategoryId, categoryName = r.CategoryName, title = r.Title,
                status = r.Status, requestedByUserId = r.RequestedByUserId, requestedByName = r.RequestedByName, requestedAt = r.RequestedAt,
                decidedAt = r.DecidedAt, doneAt = r.DoneAt,
                details = r.Details, reason = r.Reason, tokenExpiresAt = r.TokenExpiresAt, decisionNote = r.DecisionNote,
                doneByUserId = r.DoneByUserId, doneByName = r.DoneByName, doneNote = r.DoneNote,
                assignees = r.Assignees.Select(a => new { userId = a.UserId, name = a.Name }).ToList(),
                canMarkDone = v.CanMarkDone, canResend = v.CanResend
            };
        }

        // ---- the caller --------------------------------------------------------------------------------------------

        private IActionResult SignInAgain() => StatusCode(StatusCodes.Status401Unauthorized, ApiResponse.Fail("Please sign in again."));

        /// <summary>The numeric user id comes from the e-mail (real tokens carry a GUID as the name id); older tokens carried the number itself.</summary>
        private async Task<ApprovalCaller?> CallerAsync()
        {
            int? userId = await _users.ResolveUserIdAsync(User.GetEmail());
            if (userId is not > 0)
            {
                var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("id")?.Value;
                userId = int.TryParse(sub, out var n) && n > 0 ? n : null;
            }
            if (userId is null) return null;
            var super = User.IsSuperAdmin() || User.IsInRole("SuperAdmin");
            var salesExecutive = User.IsInRole("sales_executive") || User.IsInRole("SalesExecutive") || User.GetRole() == "sales_executive";
            return new ApprovalCaller(userId.Value, super, super || User.IsAdminOrSuperAdmin() || salesExecutive, User.GetLocationIds());
        }
    }
}
