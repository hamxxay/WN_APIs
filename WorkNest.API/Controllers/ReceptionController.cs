using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.DTOs.Reception;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Front desk (CMS /admin/reception, the app's Today / Meeting rooms / Network): the Receptionist's own endpoints. A
    /// Receptionist (role receptionist) works only at the locations a Super Admin assigned (JWT location_id + location_ids):
    /// a location outside them answers 403, a booking of another location 403. Admins (own locations) and super admins (all)
    /// may call them too; the tour-inquiry add also sales executives. Envelope { isSuccessful, message, data }; 400 bad
    /// input, 403 not your location, 404 not found, 409 not possible in the current state, 503 before the database
    /// update ROLE_01_receptionist (check-in / check-out) or MR_01 (meeting-room booking rules).
    /// </summary>
    [ApiController]
    [Authorize(Roles = DeskRoles)]
    [ReceptionistAllowed]
    public class ReceptionController : ControllerBase
    {
        private const string DeskRoles = "receptionist,Receptionist,admin,Admin,super_admin,SuperAdmin";
        private const string TourRoles = DeskRoles + ",sales_executive,SalesExecutive";

        private readonly IReceptionService _reception;
        private readonly IDbRepository _db;

        public ReceptionController(IReceptionService reception, IDbRepository db)
        {
            _reception = reception;
            _db = db;
        }

        /// <summary>Today at the desk: tours, meeting-room bookings, open complaints and invoices due today (own locations).</summary>
        [HttpGet("api/reception/today")]
        public Task<IActionResult> Today([FromQuery] int? locationId) =>
            Run(locationId, scope => _reception.GetTodayAsync(scope.Locations));

        /// <summary>Adds a tour inquiry by hand (walk-in / phone): the same record the website "book a tour" creates, marked as added by the caller.</summary>
        [HttpPost("api/reception/tour-inquiries")]
        [Authorize(Roles = TourRoles)]
        public async Task<IActionResult> AddTourInquiry([FromBody] TourInquiryRequest? request)
        {
            if (User.IsLocationBoundRole() && User.GetLocationIds().Count == 0) return NoLocation();
            return await Guard(() => _reception.CreateTourInquiryAsync(request, User.GetEmail(), User.GetBranchScope()));
        }

        /// <summary>Meeting rooms of the location(s) and their bookings between from and to (yyyy-MM-dd, both days included; default today).</summary>
        [HttpGet("api/reception/meeting-rooms")]
        public Task<IActionResult> MeetingRooms([FromQuery] int? locationId, [FromQuery] string? from, [FromQuery] string? to) =>
            Run(locationId, scope => _reception.GetMeetingCalendarAsync(scope.Locations, from, to));

        /// <summary>Books a meeting room for a customer with the customer's free meeting-room hours (same rules as their own booking).</summary>
        [HttpPost("api/reception/meeting-rooms/book")]
        public async Task<IActionResult> Book([FromBody] ReceptionMeetingBookRequest? request)
        {
            if (request?.CustomerId is > 0
                && !await RecordScopeAttribute.AllowsAsync(HttpContext, RecordKind.Customer, request.CustomerId.ToString()))
                return StatusCode(404, ApiResponse.Fail("Customer not found."));
            return await Run(null, scope => _reception.BookMeetingRoomAsync(request, scope.Locations, scope.UserId));
        }

        [HttpPost("api/reception/meeting-rooms/{id:int}/cancel")]
        public Task<IActionResult> Cancel(int id) => Run(null, scope => _reception.CancelMeetingRoomAsync(id, scope.Locations, scope.UserId));

        [HttpPost("api/reception/meeting-rooms/{id:int}/check-in")]
        public Task<IActionResult> CheckIn(int id) => Run(null, scope => _reception.CheckInAsync(id, scope.Locations, scope.UserId));

        [HttpPost("api/reception/meeting-rooms/{id:int}/check-out")]
        public Task<IActionResult> CheckOut(int id) => Run(null, scope => _reception.CheckOutAsync(id, scope.Locations, scope.UserId));

        /// <summary>Network status per location: online, 24 h uptime, clients now, last speed tests.</summary>
        [HttpGet("api/reception/network")]
        public Task<IActionResult> Network([FromQuery] int? locationId) =>
            Run(locationId, scope => _reception.GetNetworkAsync(scope.Locations, HttpContext.RequestAborted));

        /// <summary>Runs a speed test for the location (where its internet line is on the monitored gateway). { started, message }.</summary>
        [HttpPost("api/reception/network/speedtest")]
        public Task<IActionResult> Speedtest([FromBody] ReceptionSpeedtestRequest? request) =>
            Run(request?.LocationId, scope => _reception.StartSpeedtestAsync(request?.LocationId ?? 0, scope.Locations, HttpContext.RequestAborted));

        // ---- plumbing -------------------------------------------------------------------------------------------

        private sealed record DeskScope(IReadOnlyList<int>? Locations, int UserId);

        private IActionResult NoLocation() =>
            StatusCode(403, ApiResponse.Fail("Your account has no location yet. Ask a Super Admin to assign your locations."));

        /// <summary>
        /// Location-bound callers (receptionist, admin): their locations (the requested one must be theirs, 403 otherwise; none
        /// assigned = 403). Super admin: every location, or the requested one.
        /// </summary>
        private async Task<IActionResult> Run(int? requestedLocation, Func<DeskScope, Task<MeetingRoomOutcome>> action)
        {
            IReadOnlyList<int>? locations = null;
            if (User.IsLocationBoundRole())
            {
                var own = User.GetLocationIds();
                if (own.Count == 0) return NoLocation();
                if (requestedLocation is > 0 && !own.Contains(requestedLocation.Value))
                    return StatusCode(403, ApiResponse.Fail("You can only use the locations you are assigned to."));
                locations = requestedLocation is > 0 ? new[] { requestedLocation.Value } : own;
            }
            else if (requestedLocation is > 0) locations = new[] { requestedLocation.Value };

            var email = User.GetEmail();
            var userId = string.IsNullOrWhiteSpace(email) ? null : (await _db.GetUserIdByEmailAsync(email)).Item1;
            return await Guard(() => action(new DeskScope(locations, userId ?? 0)));
        }

        private static async Task<IActionResult> GuardCore(Func<Task<MeetingRoomOutcome>> action)
        {
            try
            {
                var o = await action();
                return new ObjectResult(new ApiResponse { IsSuccessful = o.IsSuccessful, Message = o.Message, Data = o.Data }) { StatusCode = o.StatusCode };
            }
            catch (ReceptionNotSetUpException)
            {
                return new ObjectResult(ApiResponse.Fail(ReceptionNotSetUpException.UserMessage)) { StatusCode = StatusCodes.Status503ServiceUnavailable };
            }
            catch (MeetingRoomsNotInstalledException)
            {
                return new ObjectResult(ApiResponse.Fail(MeetingRoomsNotInstalledException.UserMessage)) { StatusCode = StatusCodes.Status503ServiceUnavailable };
            }
        }

        private Task<IActionResult> Guard(Func<Task<MeetingRoomOutcome>> action) => GuardCore(action);
    }
}
