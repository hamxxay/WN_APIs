using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Challan;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;
using WorkNest.API.Filters;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Challan Validity Extension page: find a booking's challan and extend its expiry date.
    /// The extended date (WN_Bookings.ValidityDate) also moves the challan-based access suspension.
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
        public async Task<IActionResult> Search([FromQuery] string? q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(ApiResponse.Fail("Enter a booking number or challan number."));

            var row = await _challans.SearchAsync(q);
            if (row == null) return NotFound(ApiResponse.Fail("No challan found for the given search term."));
            return Ok(ApiResponse.Ok(row));
        }

        /// <summary>
        /// Extend a booking challan's expiry date (logged in WN_ChallanValidityLog).
        /// </summary>
        [HttpPost("api/challan/extend-validity")]
        public async Task<IActionResult> ExtendValidity([FromBody] ChallanExtendValidityRequest request)
        {
            var user = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? User.Identity?.Name ?? "unknown";
            var (ok, error) = await _challans.ExtendValidityAsync(request, user);
            if (!ok) return BadRequest(ApiResponse.Fail(error ?? "Could not extend the challan validity."));
            return Ok(ApiResponse.Ok(new { request.BookingId, request.NewExpiryDate }, "Challan validity extended successfully."));
        }
    }
}
