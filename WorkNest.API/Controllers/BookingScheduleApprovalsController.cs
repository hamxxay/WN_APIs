using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.RentSchedule;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Rent schedule changes of old / signed bookings that a sales executive asked for (spec section 4 item 9): the sales manager (their executives'
    /// changes and their locations) and super admin list them, approve them (optionally editing the rows first; the booking then bills from the
    /// table) or send them back with a note. Before CQ_01 every call answers 503.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "sales_manager,SalesManager,super_admin,SuperAdmin")]
    [SalesManagerAllowed]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class BookingScheduleApprovalsController : ControllerBase
    {
        private readonly IBookingScheduleService _schedule;
        private readonly IDbRepository _db;

        public BookingScheduleApprovalsController(IBookingScheduleService schedule, IDbRepository db)
        {
            _schedule = schedule;
            _db = db;
        }

        /// <summary>?status=pending (default) | all: [ { id, bookingId, customerName, spaceName, locationId, locationName, requestedById, requestedBy, requestedAt, status, decidedBy, decidedAt, note } ].</summary>
        [HttpGet("api/booking-schedule-approvals")]
        public Task<IActionResult> List([FromQuery] string? status = null, CancellationToken ct = default)
        {
            var s = (status ?? "pending").Trim().ToLowerInvariant();
            if (s != "pending" && s != "all")
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse.Fail("status must be pending or all.")));
            return Run(async caller => ApiResponse.Ok(await _schedule.ListChangesAsync(caller, s == "pending", ct)));
        }

        /// <summary>The row plus current (the booking's table now) and requested (the table with the change applied).</summary>
        [HttpGet("api/booking-schedule-approvals/{id:int}")]
        public Task<IActionResult> Get(int id, CancellationToken ct = default) =>
            Run(async caller => ApiResponse.Ok(await _schedule.GetChangeAsync(caller, id, ct)));

        /// <summary>Body { rows?: [...], note? }: applies the change (the edited rows when sent) to the booking's table.</summary>
        [HttpPost("api/booking-schedule-approvals/{id:int}/approve")]
        public Task<IActionResult> Approve(int id, [FromBody] ApproveBookingScheduleRequest? request, CancellationToken ct = default) =>
            Run(async caller =>
            {
                var (message, detail) = await _schedule.ApproveAsync(caller, id, request?.Rows, request?.Note, ct);
                return ApiResponse.Ok(detail, message);
            });

        /// <summary>Body { note } (required).</summary>
        [HttpPost("api/booking-schedule-approvals/{id:int}/send-back")]
        public Task<IActionResult> SendBack(int id, [FromBody] SendBackBookingScheduleRequest? request, CancellationToken ct = default) =>
            Run(async caller => ApiResponse.Ok(await _schedule.SendBackAsync(caller, id, request?.Note, ct), "Sent back to the sales executive."));

        private async Task<IActionResult> Run(Func<BookingScheduleCaller, Task<ApiResponse>> action)
        {
            var caller = await BookingScheduleCallerResolver.ResolveAsync(User, _db);
            if (caller == null)
                return new ObjectResult(ApiResponse.Fail("The signed-in account could not be verified.")) { StatusCode = StatusCodes.Status403Forbidden };
            if (caller.IsSalesManager && (caller.LocationIds == null || caller.LocationIds.Count == 0))
                return new ObjectResult(ApiResponse.Fail("Your account has no location yet. Ask a Super Admin to assign your locations.")) { StatusCode = StatusCodes.Status403Forbidden };
            try { return Ok(await action(caller)); }
            catch (Exception ex) { return BookingScheduleCallerResolver.ToResult(ex) ?? throw ex; }
        }
    }
}
