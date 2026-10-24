using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Challan;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;
using WorkNest.API.Filters;
using WorkNest.API.Extensions;
using WorkNest.Application.Helpers;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Challan Validity Extension page: find a booking's challan and extend its expiry date.
    /// The extended date (WN_Bookings.ValidityDate) also moves the challan-based access suspension.
    /// Location-bound admins only find and extend the challans of bookings in their own locations (search: SEC_LOC_02
    /// dbo.WN_Challan_SearchScoped, 503 until that script has run; extend: 404 for a booking of another location).
    /// </summary>
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
    [ValidateLocationScope]
    public class ChallanController : ControllerBase
    {
        private readonly IChallanService _challans;

        public ChallanController(IChallanService challans)
        {
            _challans = challans;
        }

        /// <summary>
        /// Search by booking id, booking challan number or payment voucher number.
        /// </summary>
        [HttpGet("api/challan/search")]
        public async Task<IActionResult> Search([FromQuery] string? q, [FromServices] IDbRepository db)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(ApiResponse.Fail("Enter a booking number or challan number."));

            IDictionary<string, object?>? row;
            try
            {
                // Same search as IChallanService.SearchAsync, limited to the caller's locations (null = every location).
                row = await db.SearchChallanScopedDbAsync(q.Trim(), LocationScopeLists.Csv(User.ScopedLocationIds(null)));
            }
            catch (LocationScopeUpdateMissingException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(ex.Message));
            }
            if (row == null) return NotFound(ApiResponse.Fail("No challan found for the given search term."));
            return Ok(ApiResponse.Ok(row));
        }

        /// <summary>
        /// Extend a booking challan's expiry date (logged in WN_ChallanValidityLog).
        /// </summary>
        [HttpPost("api/challan/extend-validity")]
        public async Task<IActionResult> ExtendValidity([FromBody] ChallanExtendValidityRequest request)
        {
            if (request != null && request.BookingId > 0
                && !await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Booking, request.BookingId.ToString()))
                return NotFound(ApiResponse.Fail("Booking not found."));

            var user = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? User.Identity?.Name ?? "unknown";
            var (ok, error) = await _challans.ExtendValidityAsync(request, user);
            if (!ok) return BadRequest(ApiResponse.Fail(error ?? "Could not extend the challan validity."));
            return Ok(ApiResponse.Ok(new { request.BookingId, request.NewExpiryDate }, "Challan validity extended successfully."));
        }
    }
}
