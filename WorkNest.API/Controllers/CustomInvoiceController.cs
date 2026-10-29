using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using WorkNest.API.Extensions;
using WorkNest.API.Security;
using WorkNest.Application.DTOs.CustomInvoice;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Custom invoices (CMS page Custom Invoice): a one-off invoice with its own voucher, for a customer with or without
    /// a booking, optionally replacing one of the booking's invoices. Used by a super admin, or an admin a super admin gave
    /// the CustomInvoice permission (policy "CustomInvoiceAccess"); the access list itself is super admin only.
    /// An admin works only with their own locations (customers, bookings, invoices, locations; 403 for another one).
    /// No edit or delete. Envelope { isSuccessful, message, data }, dates yyyy-MM-dd; 400 for a broken rule, 503
    /// "Database update needed: run the script CI_01_custom_invoice_TEST ..." before that database update has run.
    /// </summary>
    [ApiController]
    [Route("api/custom-invoices")]
    [Authorize]
    public class CustomInvoiceController : ControllerBase
    {
        public const string SuperAdminRoles = Roles.SuperAdmin + ",SuperAdmin";
        public const string UnknownUserMessage = "Your login could not be matched to a user account. Please sign in again.";

        private readonly ICustomInvoiceService _service;
        private readonly ICustomInvoiceAccessService _access;

        public CustomInvoiceController(ICustomInvoiceService service, ICustomInvoiceAccessService access)
        {
            _service = service;
            _access = access;
        }

        // ---- access ----------------------------------------------------------------------------------------------

        /// <summary>{ canUse, isSuperAdmin } for the signed-in user (the CMS shows the page / the access list from it).</summary>
        [HttpGet("access/me")]
        public async Task<IActionResult> AccessMe()
        {
            var caller = CustomInvoiceAccessPolicy.CallerOf(User);
            var me = await _access.CheckAsync(caller);
            return Ok(ApiResponse.Ok(me));
        }

        /// <summary>Every active admin: [{ userId, name, email, locationNames, hasAccess, grantedBy, grantedAt }].</summary>
        [HttpGet("access")]
        [Authorize(Roles = SuperAdminRoles)]
        public async Task<IActionResult> ListAccess() => Send(await _service.ListAccessAsync());

        /// <summary>{ "hasAccess": true | false } gives or takes away the admin's access; answers the admin as listed.</summary>
        [HttpPost("access/{userId:int}")]
        [Authorize(Roles = SuperAdminRoles)]
        public async Task<IActionResult> SetAccess(int userId,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CustomInvoiceAccessRequest? request)
        {
            var me = await CurrentUserIdAsync();
            if (me == null) return Unauthorized(ApiResponse.Fail(UnknownUserMessage));
            return Send(await _service.SetAccessAsync(userId, request, me.Value));
        }

        // ---- form lookups ----------------------------------------------------------------------------------------

        /// <summary>Customers by name / e-mail / code / phone (top 20): [{ userId, customerId, name, email, code, phone }].</summary>
        [HttpGet("customers")]
        [Authorize(Policy = CustomInvoiceAccessPolicy.Name)]
        public async Task<IActionResult> Customers([FromQuery] string? search = null) =>
            Send(await _service.SearchCustomersAsync(search, Scope()));

        /// <summary>The customer's bookings: [{ bookingId, spaceId, label, spaceName, locationId, locationName, startOn, endOn, status }].</summary>
        [HttpGet("bookings")]
        [Authorize(Policy = CustomInvoiceAccessPolicy.Name)]
        public async Task<IActionResult> Bookings([FromQuery] int? userId = null) =>
            Send(await _service.GetBookingsAsync(userId, Scope()));

        /// <summary>The spaces of a location a custom invoice can be assigned to: [{ spaceId, code, name, label, locationId }].</summary>
        [HttpGet("spaces")]
        [Authorize(Policy = CustomInvoiceAccessPolicy.Name)]
        public async Task<IActionResult> Spaces([FromQuery] int? locationId = null) =>
            Send(await _service.GetSpacesAsync(locationId, Scope()));

        /// <summary>The booking's invoices a custom invoice may replace: [{ invoiceId, invoiceNumber, issuedOn, periodStart, periodEnd, grandTotal, invoiceTypeId }].</summary>
        [HttpGet("replaceable")]
        [Authorize(Policy = CustomInvoiceAccessPolicy.Name)]
        public async Task<IActionResult> Replaceable([FromQuery] int? bookingId = null) =>
            Send(await _service.GetReplaceableAsync(bookingId, Scope()));

        /// <summary>Active locations: [{ id, name }].</summary>
        [HttpGet("locations")]
        [Authorize(Policy = CustomInvoiceAccessPolicy.Name)]
        public async Task<IActionResult> Locations() => Send(await _service.GetLocationsAsync(Scope()));

        /// <summary>
        /// The location's accounts on the date (today when empty): { accountReceivable, rent, servicesIncome, salesTax,
        /// securityReceived }, each { accountId, code, name } or null.
        /// </summary>
        [HttpGet("ledger-defaults")]
        [Authorize(Policy = CustomInvoiceAccessPolicy.Name)]
        public async Task<IActionResult> LedgerDefaults([FromQuery] int? locationId = null, [FromQuery] DateTime? date = null) =>
            Send(await _service.GetLedgerDefaultsAsync(locationId, date, Scope()));

        /// <summary>Accounts by code / name (top 30): [{ accountId, code, name }].</summary>
        [HttpGet("accounts")]
        [Authorize(Policy = CustomInvoiceAccessPolicy.Name)]
        public async Task<IActionResult> Accounts([FromQuery] string? search = null) =>
            Send(await _service.SearchAccountsAsync(search));

        // ---- invoices --------------------------------------------------------------------------------------------

        /// <summary>
        /// Writes the invoice and its voucher: { invoiceId, invoiceNumber, publicId, voucherId, voucherCode,
        /// replacedInvoiceNumber, reversalVoucherCode }. The rules are in CustomInvoiceService.Validate.
        /// </summary>
        [HttpPost("")]
        [Authorize(Policy = CustomInvoiceAccessPolicy.Name)]
        public async Task<IActionResult> Create([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CustomInvoiceCreateRequest? request)
        {
            var me = await CurrentUserIdAsync();
            if (me == null) return Unauthorized(ApiResponse.Fail(UnknownUserMessage));
            return Send(await _service.CreateAsync(request, me.Value, Scope()));
        }

        /// <summary>Custom invoices, newest first: { items: [...], total }.</summary>
        [HttpGet("")]
        [Authorize(Policy = CustomInvoiceAccessPolicy.Name)]
        public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] string? search = null, [FromQuery] int pageSize = 20) =>
            Send(await _service.GetListAsync(page, search, Scope(), pageSize));

        // ---- helpers ---------------------------------------------------------------------------------------------

        private IActionResult Send(CustomInvoiceResult result) => StatusCode(result.StatusCode, result.Response);

        /// <summary>Every location for a super admin; otherwise the caller's own (JWT location_id + location_ids).</summary>
        private CustomInvoiceScope Scope() => ScopeOf(User);

        public static CustomInvoiceScope ScopeOf(ClaimsPrincipal user) =>
            CustomInvoiceAccessPolicy.CallerOf(user).IsSuperAdmin ? CustomInvoiceScope.All : CustomInvoiceScope.Of(user.GetLocationIds());

        /// <summary>
        /// The signed-in user's WN_Users.Id. The token carries the public id (a GUID) and e-mail, so the user is looked up
        /// by e-mail, as the fee receipt / complaints controllers do; older tokens carried the numeric id.
        /// </summary>
        private async Task<int?> CurrentUserIdAsync()
        {
            var id = await _access.ResolveUserIdAsync(User.GetEmail());
            if (id is > 0) return id;
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("id")?.Value;
            return int.TryParse(idClaim, out var n) && n > 0 ? n : null;
        }
    }
}
