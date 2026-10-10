using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkNest.Application.DTOs.Attendant;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;
using WorkNest.API.Extensions;

namespace WorkNest.API.Controllers
{
    /// <summary>Door access for logged-in app users (customers' staff added as access users in Sales).</summary>
    [ApiController]
    [Authorize]
    public class MobileAccessController : ControllerBase
    {
        private readonly IAttendantService _attendants;
        private readonly IHikMobileDoorService _doors;
        private readonly IDbRepository _db;

        /// <summary>How long an app verification lasts before the user does Access Request again.</summary>
        private const int AppVerificationDays = 30;

        public MobileAccessController(IAttendantService attendants, IHikMobileDoorService doors, IDbRepository db)
        {
            _attendants = attendants;
            _doors = doors;
            _db = db;
        }

        /// <summary>
        /// The person behind an app unlock: the details must match an access user again (same rule as verify), the
        /// room must be one of theirs with access on, and when the company registered an email for them it must be the
        /// email of the logged-in account — knowing someone's CNIC is not enough to open their door.
        /// </summary>
        private async Task<(int? PersonId, IActionResult? Error)> ResolveDoorUserAsync(MobileDoorRequest request)
        {
            var verified = await _attendants.VerifyMobileAccessAsync(request);
            if (!verified.Matched || verified.PersonId is not int personId)
                return (null, Ok(ApiResponse.Fail(verified.Message)));
            var account = User.GetEmail();
            if (!string.IsNullOrWhiteSpace(verified.PersonEmail)
                && !string.Equals(verified.PersonEmail, account?.Trim(), StringComparison.OrdinalIgnoreCase))
                return (null, Ok(ApiResponse.Fail("Sign in with the email your company registered for you to unlock doors.")));
            if (!verified.Spaces.Any(sp => sp.BookingDetailId == request.BookingDetailId && sp.IsEnabled))
                return (null, Ok(ApiResponse.Fail("Your access to this room is turned off. Contact your company admin or reception.")));
            return (personId, null);
        }

        /// <summary>
        /// Access Request completed in the app: records the verification (person, login account, room, 30 days) so the
        /// web shows "App verified" next to the user. Same checks as unlocking.
        /// </summary>
        [HttpPost("api/mobile/access/confirm")]
        public async Task<IActionResult> Confirm([FromBody] MobileDoorRequest request)
        {
            var (personId, error) = await ResolveDoorUserAsync(request ?? new MobileDoorRequest());
            if (error != null) return error;
            var now = DateTime.UtcNow;
            var expires = now.AddDays(AppVerificationDays);
            var saved = await _db.UpsertMobileAccessVerificationDbAsync(personId!.Value, request!.BookingDetailId, User.GetEmail()!.Trim(), now, expires);
            return Ok(ApiResponse.Ok(new { verifiedAt = now, expiresAt = expires, recorded = saved }, "Access verified."));
        }

        /// <summary>The room door the verified access user can unlock from the app (the booked room's machine only).</summary>
        [HttpPost("api/mobile/access/doors")]
        // Logged-in users only: the per-user default limit applies (a per-IP limit would throttle the whole office Wi-Fi).
        public async Task<IActionResult> Doors([FromBody] MobileDoorRequest request)
        {
            var (personId, error) = await ResolveDoorUserAsync(request ?? new MobileDoorRequest());
            if (error != null) return error;
            var result = await _doors.GetDoorsAsync(request!.BookingDetailId, personId!.Value);
            return Ok(result.Ok ? ApiResponse.Ok(result, result.Message) : ApiResponse.Fail(result.Message));
        }

        /// <summary>Unlocks their room door now (remote open on the machine); entrances are refused; every attempt is logged.</summary>
        [HttpPost("api/mobile/access/open")]
        // Logged-in users only: the per-user default limit applies (a per-IP limit would throttle the whole office Wi-Fi).
        public async Task<IActionResult> Open([FromBody] MobileDoorRequest request)
        {
            if (request?.DeviceId is not int deviceId) return Ok(ApiResponse.Fail("Choose the door to unlock."));
            var (personId, error) = await ResolveDoorUserAsync(request);
            if (error != null) return error;
            var result = await _doors.OpenDoorAsync(request.BookingDetailId, personId!.Value, deviceId, User.GetEmail());
            if (result.Ok)
            {
                try { await _db.TouchMobileAccessUnlockDbAsync(personId.Value, User.GetEmail()!.Trim(), DateTime.UtcNow); }
                catch { /* the unlock itself already happened and is logged */ }
            }
            return Ok(result.Ok ? ApiResponse.Ok(result, result.Message) : ApiResponse.Fail(result.Message));
        }

        /// <summary>
        /// Checks the entered name, email, CNIC (and phone, if given) against booking access users.
        /// Rate limited like login, since it answers "does this CNIC + email exist".
        /// </summary>
        [HttpPost("api/mobile/access/verify")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Verify([FromBody] MobileAccessVerifyRequest request)
        {
            // CNIC stays optional here (email can be used instead), but when entered it must be a real CNIC.
            // Only checked on Verify (where it is typed in): door/open re-checks keep accepting what was verified before.
            var cnicError = WorkNest.Application.Helpers.Cnic.Validate(request?.Cnic);
            if (cnicError != null) return Ok(ApiResponse.Fail(cnicError));
            var result = await _attendants.VerifyMobileAccessAsync(request ?? new MobileAccessVerifyRequest());
            return Ok(ApiResponse.Ok(result, result.Message));
        }
    }
}
