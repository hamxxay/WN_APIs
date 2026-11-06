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
    
        // =========================================================================================
        // Phase 2: Branch Transfers & Pending Payments Endpoints
        // =========================================================================================

        /// <summary>
        /// Initiates a new branch transfer request (sending side).
        /// </summary>
        [HttpPost("requests")]
        public async Task<IActionResult> CreateRequest([FromBody] BranchTransferCreateRequestDto request)
        {
            if (request == null)
                return BadRequest(ApiResponse.Fail("Request payload is required."));

            var actorId = await ResolveActorIdAsync();
            var userEmail = ResolveUserEmail();
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;

            var result = await _transferService.CreateBranchTransferRequestAsync(request, userEmail, actorId, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Lists outgoing branch transfer requests for the sending location/company.
        /// </summary>
        [HttpGet("requests")]
        public async Task<IActionResult> ListSendingRequests(
            [FromQuery] int? companyId,
            [FromQuery] int? locationId)
        {
            var userRole = User.GetRole();
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;

            var result = await _transferService.ListSendingRequestsAsync(companyId, locationId, userRole, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Cancels a pending transfer request.
        /// </summary>
        [HttpPost("requests/{id:int}/cancel")]
        public async Task<IActionResult> CancelRequest(int id, [FromBody] BranchTransferCancelRequestDto request)
        {
            var actorId = await ResolveActorIdAsync();
            var userEmail = ResolveUserEmail();
            var userRole = User.GetRole();
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;

            var result = await _transferService.CancelBranchTransferRequestAsync(id, request ?? new(), userEmail, actorId, userRole, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Lists incoming transfer requests for the destination branch (CAPACITY ONLY).
        /// </summary>
        [HttpGet("incoming")]
        public async Task<IActionResult> ListIncomingRequests(
            [FromQuery] int? companyId,
            [FromQuery] int? locationId)
        {
            var userRole = User.GetRole();
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;

            var result = await _transferService.ListIncomingRequestsAsync(companyId, locationId, userRole, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Fetches available destination spaces free for the remaining term with capacity >= requested (capacity-only safe).
        /// </summary>
        [HttpGet("incoming/{id:int}/available-spaces")]
        public async Task<IActionResult> GetIncomingAvailableSpaces(int id)
        {
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;
            var result = await _transferService.GetIncomingAvailableSpacesAsync(id, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Accepts an incoming request by selecting a destination space.
        /// </summary>
        [HttpPost("incoming/{id:int}/accept")]
        public async Task<IActionResult> AcceptWithSpace(int id, [FromBody] BranchTransferAcceptWithSpaceRequestDto request)
        {
            if (request == null || request.SpaceId <= 0)
                return BadRequest(ApiResponse.Fail("SpaceId is required to accept transfer."));

            var actorId = await ResolveActorIdAsync();
            var userEmail = ResolveUserEmail();
            var userRole = User.GetRole();
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;

            var result = await _transferService.AcceptWithSpaceAsync(id, request, userEmail, actorId, userRole, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Declines an incoming transfer request.
        /// </summary>
        [HttpPost("incoming/{id:int}/decline")]
        public async Task<IActionResult> DeclineRequest(int id, [FromBody] BranchTransferDeclineRequestDto request)
        {
            var actorId = await ResolveActorIdAsync();
            var userEmail = ResolveUserEmail();
            var userRole = User.GetRole();
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;

            var result = await _transferService.DeclineBranchTransferRequestAsync(id, request ?? new(), userEmail, actorId, userRole, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Retrieves match resolution details for receiving admin.
        /// </summary>
        [HttpGet("incoming/{id:int}/match-resolution")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
        public async Task<IActionResult> GetMatchResolution(int id)
        {
            var userRole = User.GetRole();
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;

            var result = await _transferService.GetMatchResolutionAsync(id, userRole, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Resolves customer match and finalizes the branch transfer.
        /// </summary>
        [HttpPost("incoming/{id:int}/resolve-match")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
        public async Task<IActionResult> ResolveMatch(int id, [FromBody] BranchTransferResolveMatchRequestDto request)
        {
            if (request == null)
                return BadRequest(ApiResponse.Fail("Request payload is required."));

            var actorId = await ResolveActorIdAsync();
            var userEmail = ResolveUserEmail();
            var userRole = User.GetRole();
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;

            var result = await _transferService.ResolveMatchAsync(id, request, userEmail, actorId, userRole, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Lists pending transfer payments for either branch.
        /// </summary>
        [HttpGet("pending-payments")]
        public async Task<IActionResult> ListPendingPayments(
            [FromQuery] int? companyId,
            [FromQuery] int? locationId)
        {
            var userRole = User.GetRole();
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;

            var result = await _transferService.ListPendingPaymentsAsync(companyId, locationId, userRole, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Confirms a pending transfer payment.
        /// </summary>
        [HttpPost("pending-payments/{id:int}/confirm")]
        public async Task<IActionResult> ConfirmPendingPayment(int id, [FromBody] PendingPaymentConfirmRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.BankAccount) || string.IsNullOrWhiteSpace(request.PaymentReference))
                return BadRequest(ApiResponse.Fail("Bank account and payment reference are required."));

            var actorId = await ResolveActorIdAsync();
            var userEmail = ResolveUserEmail();
            var userRole = User.GetRole();
            var allowedLocations = User.IsLocationBoundRole() ? User.GetLocationIds() : null;

            var result = await _transferService.ConfirmPendingPaymentAsync(id, request, userEmail, actorId, userRole, allowedLocations);
            return result.IsSuccessful ? Ok(result) : BadRequest(result);
        }
    }
}

