using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.Transfer;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Route("api/booking-transfers")]
    [Authorize(Roles = "sales_executive,SalesExecutive,admin,Admin,super_admin,SuperAdmin")]
    [ValidateLocationScope]
    public class TransferController : ControllerBase
    {
        private readonly ITransferService _transferService;
        private readonly IDbRepository _db;

        public TransferController(ITransferService transferService, IDbRepository db)
        {
            _transferService = transferService;
            _db = db;
        }

        private string? ResolveUserEmail() => User.GetEmail();

        private async Task<int?> ResolveActorIdAsync()
        {
            var email = ResolveUserEmail();
            if (string.IsNullOrWhiteSpace(email)) return null;
            var userRow = await _db.GetUserByEmailAsync(email);
            return userRow?.TryGetValue("Id", out var id) == true && id != null ? Convert.ToInt32(id) : null;
        }


        /// <summary>
        /// Lists active bookings eligible for transfer, optionally filtered by location and search.
        /// </summary>
        [HttpGet("active-bookings")]
        public async Task<IActionResult> ActiveBookings(
            [FromQuery] int? locationId,
            [FromQuery] string? search)
        {
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            var result = await _transferService.GetActiveBookingsAsync(locationId, search, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }
        /// <summary>
        /// Previews space transfer settlement, discount cap check, and blocking invoices.
        /// </summary>
        [HttpGet("preview")]
        public async Task<IActionResult> PreviewGet(
            [FromQuery] int bookingId,
            [FromQuery] int newSpaceId,
            [FromQuery] DateTime? effectiveDate,
            [FromQuery] decimal transferFee = 0m)
        {
            var request = new TransferPreviewRequest
            {
                BookingId = bookingId,
                NewSpaceId = newSpaceId,
                EffectiveDate = effectiveDate,
                TransferFee = transferFee
            };

            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            var result = await _transferService.GetPreviewAsync(request, ResolveUserEmail(), allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Previews space transfer settlement via POST.
        /// </summary>
        [HttpPost("preview")]
        public async Task<IActionResult> PreviewPost([FromBody] TransferPreviewRequest request)
        {
            if (request == null)
                return BadRequest(ApiResponse.Fail("Request payload is required."));

            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            var result = await _transferService.GetPreviewAsync(request, ResolveUserEmail(), allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Fetches available target spaces in the same location for the remaining term.
        /// </summary>
        [HttpGet("available-spaces")]
        public async Task<IActionResult> AvailableSpaces(
            [FromQuery] int bookingId,
            [FromQuery] DateTime? effectiveDate)
        {
            if (bookingId <= 0)
                return BadRequest(ApiResponse.Fail("Booking ID is required."));

            var effDate = effectiveDate ?? DateTime.Today;
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            var result = await _transferService.GetAvailableTargetSpacesAsync(bookingId, effDate, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Executes space transfer transactionally.
        /// </summary>
        [HttpPost("execute")]
        public async Task<IActionResult> Execute([FromBody] TransferExecuteRequest request)
        {
            if (request == null)
                return BadRequest(ApiResponse.Fail("Transfer request payload is required."));

            var actorId = await ResolveActorIdAsync();
            var userEmail = ResolveUserEmail();
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;

            var result = await _transferService.ExecuteTransferAsync(request, userEmail, actorId, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }
    }
}

