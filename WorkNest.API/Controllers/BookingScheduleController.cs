using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.RentSchedule;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// A booking's rent schedule (spec 2026-11-19, section 4 items 8): GET shows the table (the stored one, or for an old booking the projection of
    /// its remaining months with the invoiced months locked); PUT saves it: admin / super admin directly, a sales executive as a change the sales
    /// manager approves. Location-bound staff only see bookings of their locations. Before CQ_01 every call answers 503.
    /// </summary>
    [ApiController]
    [Authorize]
    [RecordScope(RecordKind.Booking, "id")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class BookingScheduleController : ControllerBase
    {
        private const string StaffRoles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive,sales_manager,SalesManager";
        private const string EditRoles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive";

        private readonly IBookingScheduleService _schedule;
        private readonly IDbRepository _db;

        public BookingScheduleController(IBookingScheduleService schedule, IDbRepository db)
        {
            _schedule = schedule;
            _db = db;
        }

        /// <summary>{ hasSchedule, pendingChange: { id, requestedBy, requestedAt } | null, schedule }.</summary>
        [HttpGet("api/booking/{id:int}/schedule")]
        [Authorize(Roles = StaffRoles)]
        [SalesManagerAllowed]
        public Task<IActionResult> Get(int id, CancellationToken ct = default) =>
            Run(async () => ApiResponse.Ok(await _schedule.GetAsync(id, ct)));

        /// <summary>Body { rows: [{ rowNo, discountType, discountValue, securityDeposit }] }. Admin / super admin: saved (the booking then bills from the table). Sales executive: { pendingApproval: true }.</summary>
        [HttpPut("api/booking/{id:int}/schedule")]
        [Authorize(Roles = EditRoles)]
        public Task<IActionResult> Put(int id, [FromBody] SaveBookingScheduleRequest? request, CancellationToken ct = default) =>
            Run(async () =>
            {
                var caller = await BookingScheduleCallerResolver.ResolveAsync(User, _db);
                if (caller == null) throw new BookingScheduleForbiddenException("The signed-in account could not be verified.");
                var result = await _schedule.SaveAsync(caller, id, request?.Rows, ct);
                return result.PendingApproval
                    ? ApiResponse.Ok(new { pendingApproval = true, changeId = result.ChangeId }, "Sent to the sales manager for approval.")
                    : ApiResponse.Ok(new { pendingApproval = false, hasSchedule = result.Schedule?.HasSchedule, schedule = result.Schedule?.Schedule }, "Rent schedule saved.");
            });

        private async Task<IActionResult> Run(Func<Task<ApiResponse>> action)
        {
            try { return Ok(await action()); }
            catch (Exception ex) { return BookingScheduleCallerResolver.ToResult(ex) ?? throw ex; }
        }
    }

    /// <summary>Resolves the caller of the booking schedule endpoints and maps the schedule exceptions to answers.</summary>
    internal static class BookingScheduleCallerResolver
    {
        public static async Task<BookingScheduleCaller?> ResolveAsync(System.Security.Claims.ClaimsPrincipal user, IDbRepository db)
        {
            var email = user.GetEmail();
            var row = string.IsNullOrWhiteSpace(email) ? null : await db.GetUserByEmailAsync(email);
            if (row == null || !row.TryGetValue("Id", out var id) || id == null) return null;
            int userId = Convert.ToInt32(id);
            bool superAdmin = user.IsSuperAdmin() || user.IsInRole("super_admin") || user.IsInRole("SuperAdmin");
            bool admin = !superAdmin && user.IsAdminOrSuperAdmin();
            bool exec = !superAdmin && !admin && (user.IsInRole("sales_executive") || user.IsInRole("SalesExecutive"));
            bool manager = !superAdmin && user.IsSalesManager();
            return new BookingScheduleCaller(userId, superAdmin, admin, exec, manager, user.GetLocationIds());
        }

        private static IActionResult Status(int code, string message) => new ObjectResult(ApiResponse.Fail(message)) { StatusCode = code };

        public static IActionResult? ToResult(Exception ex) => ex switch
        {
            BookingScheduleNotSetUpException e => Status(StatusCodes.Status503ServiceUnavailable, e.Message),
            BookingScheduleNotFoundException e => new NotFoundObjectResult(ApiResponse.Fail(e.Message)),
            BookingScheduleForbiddenException e => Status(StatusCodes.Status403Forbidden, e.Message),
            BookingScheduleConflictException e => new ConflictObjectResult(ApiResponse.Fail(e.Message)),
            BookingScheduleRuleException e => new BadRequestObjectResult(ApiResponse.Fail(e.Message)),
            ArgumentException e => new BadRequestObjectResult(ApiResponse.Fail(e.Message)),
            InvalidOperationException e => new BadRequestObjectResult(ApiResponse.Fail(e.Message)),
            _ => null
        };
    }
}
