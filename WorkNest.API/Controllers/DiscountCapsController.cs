using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>Request body of PUT api/admin/discount-caps/{id}.</summary>
    public class DiscountCapSaveRequest
    {
        public decimal? DiscountCap { get; set; }
    }

    /// <summary>
    /// Discount Caps (CMS Administration), super admin only: the manual-discount cap per offering type per location
    /// (dbo.WN_OfferingType.DiscountCap). Nothing is cached, so a saved cap applies to the next quotation / booking at once.
    /// </summary>
    [ApiController]
    [Route("api/admin/discount-caps")]
    [Authorize(Roles = ServiceSwitchController.SuperAdminRoles)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class DiscountCapsController : ControllerBase
    {
        private readonly IDiscountCapsRepository _caps;

        public DiscountCapsController(IDiscountCapsRepository caps) => _caps = caps;

        /// <summary>Offering types grouped by location: { locationId, locationName, items: [{ id, description, discountCap, isActive, updatedBy, updatedAt }] }.</summary>
        [HttpGet]
        public Task<IActionResult> List(CancellationToken ct = default) => Run(async () =>
            Ok(ApiResponse.Ok(DiscountCapRules.Group(await _caps.GetAllAsync(ct), await _caps.GetLocationsAsync(ct)))));

        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, [FromBody] DiscountCapSaveRequest? request, CancellationToken ct = default) => Run(async () =>
        {
            if (request == null) return BadRequest(ApiResponse.Fail("Request body is required."));
            var error = DiscountCapRules.Validate(request.DiscountCap);
            if (error != null) return BadRequest(ApiResponse.Fail(error));
            var row = await _caps.UpdateAsync(id, request.DiscountCap!.Value, User.GetEmail(), ct);
            if (row == null) return NotFound(ApiResponse.Fail("Offering type not found."));
            return Ok(ApiResponse.Ok(DiscountCapRules.ToItem(row), "Discount cap saved."));
        });

        private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try { return await action(); }
            catch (DiscountCapsMissingException) { return StatusCode(503, ApiResponse.Fail(DiscountCapsMissingException.UserMessage)); }
        }
    }
}
