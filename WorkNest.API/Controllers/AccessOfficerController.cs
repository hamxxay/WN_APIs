using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.AccessOfficer;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Access Officer page (CMS /admin/access and the app's Access screen): door access by CNIC, nothing else. An Access
    /// Officer (role access_officer) works only at the locations a Super Admin assigned (JWT location_id + location_ids);
    /// super admins may open it too (every location). Every call checks that the booked space is at one of the caller's
    /// locations (403 otherwise). Envelope { isSuccessful, message, data }; 400 bad input, 404 not found, 409 not
    /// possible in the current state; 503 before the database update AO_01 has run.
    /// The enrol calls wait for the person at the room machine (up to about 90 seconds): nothing in the API pipeline
    /// limits a request's duration (IIS in-process hosting, no request timeout middleware; the rate limiter only counts
    /// requests), so clients must just allow a long request.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "access_officer,super_admin,SuperAdmin")]
    [AccessOfficerAllowed]
    public class AccessOfficerController : ControllerBase
    {
        private readonly IAccessOfficerService _access;
        private readonly IBookingEmployeeService _employees;
        private readonly ILogger<AccessOfficerController> _logger;

        public AccessOfficerController(IAccessOfficerService access, IBookingEmployeeService employees, ILogger<AccessOfficerController> logger)
        {
            _access = access;
            _employees = employees;
            _logger = logger;
        }

        /// <summary>The person with this CNIC / passport number and every booked space of the caller's locations they are on.</summary>
        [HttpGet("api/access-officer/lookup")]
        public Task<IActionResult> Lookup([FromQuery] string? cnic) =>
            Run(scope => _access.LookupAsync(cnic, scope));

        /// <summary>A customer-added person ("pending"): the sales CNIC check, which puts them on the room.</summary>
        [HttpPost("api/access-officer/add-to-room")]
        public Task<IActionResult> AddToRoom([FromBody] AccessOfficerAddToRoomRequest? request) =>
            Run(scope => _access.AddToRoomAsync(request, scope));

        /// <summary>kind = face | fingerprint: capture on the room machine, copied to every Entrance machine.</summary>
        [HttpPost("api/access-officer/enroll/{kind}")]
        public Task<IActionResult> Enroll(string kind, [FromBody] AccessOfficerEnrollRequest? request) =>
            Run(scope => _access.EnrollAsync(kind, request, scope));

        /// <summary>Door access off (room + Entrance machines); the person stays on the booking.</summary>
        [HttpPost("api/access-officer/remove-access")]
        public Task<IActionResult> RemoveAccess([FromBody] AccessOfficerPersonRequest? request) =>
            Run(scope => _access.SetAccessAsync(request, false, scope));

        /// <summary>Door access on again.</summary>
        [HttpPost("api/access-officer/restore-access")]
        public Task<IActionResult> RestoreAccess([FromBody] AccessOfficerPersonRequest? request) =>
            Run(scope => _access.SetAccessAsync(request, true, scope));

        private async Task<IActionResult> Run(Func<AccessOfficerScope, Task<AccessOfficerOutcome>> action)
        {
            // A super admin covers every location; an Access Officer only their own.
            IReadOnlyCollection<int>? locations = null;
            var superAdmin = User.IsInRole("super_admin") || User.IsInRole("SuperAdmin") || User.IsSuperAdmin();
            if (!superAdmin)
            {
                locations = User.GetLocationIds();
                if (locations.Count == 0)
                    return Result(403, "Your account has no location yet. Ask a Super Admin to assign your locations.");
            }

            try
            {
                var userId = await _employees.ResolveUserIdAsync(User.GetEmail(), User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                var outcome = await action(new AccessOfficerScope(locations, userId));
                return Result(outcome.StatusCode, outcome.Message, outcome.Data, outcome.IsSuccessful);
            }
            catch (AccessOfficerDatabaseUpdateMissingException ex)
            {
                _logger.LogWarning("Access Officer page called before the database update AO_01: {Error}", ex.Detail);
                return Result(StatusCodes.Status503ServiceUnavailable, ex.Message);
            }
            catch (BookingEmployeesNotInstalledException)
            {
                return Result(StatusCodes.Status503ServiceUnavailable, BookingEmployeesNotInstalledException.UserMessage);
            }
            catch (InvalidOperationException ex)
            {
                // The room's own rule refused the assignment (e.g. it filled up meanwhile).
                return Result(StatusCodes.Status400BadRequest, ex.Message);
            }
        }

        private static IActionResult Result(int status, string message, object? data = null, bool ok = false) =>
            new ObjectResult(new ApiResponse { IsSuccessful = ok, Message = message, Data = data }) { StatusCode = status };
    }
}
