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
        private readonly IFeeReceiptService _service;

        public FeeReceiptController(IFeeReceiptService service)
        {
            _service = service;
        }

        private int GetCurrentUserId()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("id")?.Value
                       ?? User.FindFirst("userId")?.Value
                       ?? User.FindFirst("sub")?.Value;

            return int.TryParse(idClaim, out var id) ? id : 0;
        }

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
        /// Search eligible unpaid non-cancelled invoices with no active receipt.
        /// </summary>
        [HttpGet("unpaid-invoices")]
        public async Task<IActionResult> GetUnpaidInvoices(
            [FromQuery] string? search = null,
            [FromQuery] int? locationId = null,
            [FromQuery] int top = 50)
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
        /// Create a fee receipt for an unpaid invoice and mark the invoice paid.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] FeeReceiptCreateDto request)
        {
            if (request == null)
                return BadRequest(ApiResponse.Fail("Invalid request body."));

            var userId = GetCurrentUserId();
            var allowedLocations = ResolveAllowedLocations();
            var isSuperAdmin = User.IsSuperAdmin();

            var result = await _service.CreateReceiptAsync(request, userId, allowedLocations, isSuperAdmin);
            if (!result.IsSuccessful)
                return BadRequest(result);

            return StatusCode(201, result);
        }

        /// <summary>
        /// Voids an active fee receipt with a required reason, restoring the invoice to unpaid.
        /// </summary>
        [HttpPost("void")]
        public async Task<IActionResult> Void([FromBody] FeeReceiptVoidDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.VoidReason))
                return BadRequest(ApiResponse.Fail("A void reason is required."));

            var userId = GetCurrentUserId();
            var result = await _service.VoidReceiptAsync(request, userId);
            if (!result.IsSuccessful)
                return BadRequest(result);

            return Ok(result);
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
        /// Gets a fee receipt by integer Id.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _service.GetByIdAsync(id, null);
            if (!result.IsSuccessful)
                return NotFound(result);

            return Ok(result);
        }

        /// <summary>
        /// Gets a fee receipt by GUID.
        /// </summary>
        [HttpGet("guid/{guid:guid}")]
        public async Task<IActionResult> GetByGuid(Guid guid)
        {
            var result = await _service.GetByIdAsync(null, guid);
            if (!result.IsSuccessful)
                return NotFound(result);

            return Ok(result);
        }
    }
}
