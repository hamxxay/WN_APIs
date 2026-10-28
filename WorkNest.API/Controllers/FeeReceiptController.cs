using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.Application.DTOs.FeeReceipt;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")]
    [Route("api/fee-receipts")]
    public class FeeReceiptController : ControllerBase
    {
        private const string AdminRoles = "admin,Admin,super_admin,SuperAdmin";

        private readonly IFeeReceiptService _service;
        private readonly IDbRepository _db;

        public FeeReceiptController(IFeeReceiptService service, IDbRepository db)
        {
            _service = service;
            _db = db;
        }

        /// <summary>
        /// The signed-in user's WN_Users.Id (for CreatedById / UpdatedById), or null if they can't be matched.
        /// The token carries the user's public id (a GUID) and email, not the numeric id, so the user is looked up
        /// by email (dbo.WN_Users_GetByEmail), as the complaints / contacts / WhatsApp controllers do.
        /// </summary>
        private async Task<int?> GetCurrentUserIdAsync()
        {
            var email = User.GetEmail();
            if (!string.IsNullOrWhiteSpace(email))
            {
                var (id, _) = await _db.GetUserIdByEmailAsync(email);
                if (id is > 0) return id;
            }
            // Older tokens that carried the numeric id directly.
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("id")?.Value;
            return int.TryParse(idClaim, out var n) && n > 0 ? n : null;
        }

        /// <summary>Maps a service result to its HTTP status (400 / 403 / 404), keeping the ApiResponse body.</summary>
        private IActionResult ToActionResult(FeeReceiptResult result, int successStatus = 200) => result.Outcome switch
        {
            FeeReceiptOutcome.Ok => StatusCode(successStatus, result.Response),
            FeeReceiptOutcome.NotFound => NotFound(result.Response),
            FeeReceiptOutcome.Forbidden => StatusCode(403, result.Response),
            _ => BadRequest(result.Response)
        };

        private List<int>? ResolveAllowedLocations()
        {
            if (User.IsSuperAdmin())
                return null; // Super Admin covers all locations

            if (User.IsLocationBoundRole())
            {
                var locIds = User.GetLocationIds();
                return locIds.Count > 0 ? locIds : new List<int> { -1 };
            }

            return null;
        }

        /// <summary>
        /// Invoices that can take a receipt: not cancelled, with a balance left (unpaid, overdue or part-paid), with their
        /// amount paid so far, balance and whether they can be paid in parts.
        /// </summary>
        [HttpGet("unpaid-invoices")]
        public async Task<IActionResult> GetUnpaidInvoices(
            [FromQuery] string? search = null,
            [FromQuery] int? locationId = null,
            [FromQuery] int top = 200)
        {
            var allowedLocations = ResolveAllowedLocations();
            var result = await _service.GetUnpaidInvoicesAsync(search, locationId, allowedLocations, top);
            return Ok(result);
        }

        /// <summary>
        /// Returns Cash and Bank accounts from AccountsCOA, optionally filtered by invoice CompanyId.
        /// </summary>
        [HttpGet("accounts")]
        public async Task<IActionResult> GetAccounts([FromQuery] int? companyId = null)
        {
            var result = await _service.GetAccountsAsync(companyId);
            return Ok(result);
        }

        /// <summary>
        /// Create a fee receipt for an invoice: the whole balance (PaymentMode Full, the default) or a part of it (Partial
        /// with Amount). The invoice becomes Paid when nothing is left, else Partial.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] FeeReceiptCreateDto request)
        {
            if (request == null)
                return BadRequest(ApiResponse.Fail("Invalid request body."));

            var userId = await GetCurrentUserIdAsync();
            if (userId == null)
                return Unauthorized(ApiResponse.Fail("Your login could not be matched to a user account. Please sign in again."));

            var allowedLocations = ResolveAllowedLocations();
            var isSuperAdmin = User.IsSuperAdmin();

            var result = await _service.CreateReceiptAsync(request, userId.Value, allowedLocations, isSuperAdmin);
            return ToActionResult(result, 201);
        }

        /// <summary>
        /// Voids an active fee receipt with a required reason, restoring the invoice to unpaid
        /// (only while it is still paid by this receipt). Admin / super admin only, within their locations.
        /// </summary>
        [HttpPost("void")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Void([FromBody] FeeReceiptVoidDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.VoidReason))
                return BadRequest(ApiResponse.Fail("A void reason is required."));

            var userId = await GetCurrentUserIdAsync();
            if (userId == null)
                return Unauthorized(ApiResponse.Fail("Your login could not be matched to a user account. Please sign in again."));

            var result = await _service.VoidReceiptAsync(request, userId.Value, ResolveAllowedLocations());
            return ToActionResult(result);
        }

        /// <summary>
        /// Paginated list of fee receipts with optional search, location, and status filters.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetList(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null,
            [FromQuery] int? locationId = null,
            [FromQuery] byte? status = null)
        {
            var allowedLocations = ResolveAllowedLocations();
            var result = await _service.GetListAsync(page, limit, search, locationId, status, allowedLocations);
            return Ok(result);
        }

        /// <summary>
        /// Gets a fee receipt by integer Id. 404 when it does not exist or is outside the caller's locations.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _service.GetByIdAsync(id, null, ResolveAllowedLocations());
            return ToActionResult(result);
        }

        /// <summary>
        /// Gets a fee receipt by GUID.
        /// </summary>
        [HttpGet("guid/{guid:guid}")]
        public async Task<IActionResult> GetByGuid(Guid guid)
        {
            var result = await _service.GetByIdAsync(null, guid, ResolveAllowedLocations());
            return ToActionResult(result);
        }
    }
}
