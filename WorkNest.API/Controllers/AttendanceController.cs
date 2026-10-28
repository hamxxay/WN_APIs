using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.Application.DTOs.Attendance;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// The customer's own attendance (website /my-attendance, app Attendance). Only the customer's MAIN account (the user
    /// linked to WN_Customers.UserId) and only when staff turned attendance on for the customer; anyone else (also an
    /// employee with their own login) gets 403, except api/attendance/my/status which answers enabled = false so the
    /// menu item stays hidden. Before the ATT_01 database update: 503.
    /// </summary>
    [ApiController]
    [Authorize] // ownership is checked per booking / customer, so a staff account that is also a customer works too
    public class AttendanceController : ControllerBase
    {
        public const string NotEnabledMessage = "Attendance is not enabled for your account";

        private readonly IAttendanceService _attendance;
        private readonly IBookingEmployeeService _employees;

        public AttendanceController(IAttendanceService attendance, IBookingEmployeeService employees)
        {
            _attendance = attendance;
            _employees = employees;
        }

        /// <summary>Whether attendance is on for the signed-in customer (enabled = false for everyone else).</summary>
        [HttpGet("api/attendance/my/status")]
        public Task<IActionResult> Status() =>
            Guarded(async () =>
            {
                var customer = await MyCustomerAsync();
                return Ok(ApiResponse.Ok(new AttendanceStatusDto
                {
                    Enabled = customer?.Enabled == true,
                    CustomerId = customer?.CustomerId,
                    CustomerName = customer?.CustomerName
                }));
            });

        /// <summary>Daily report from .. to (YYYY-MM-DD, default this month up to today, at most 62 days); personId narrows to one employee.</summary>
        [HttpGet("api/attendance/my/report")]
        public Task<IActionResult> Report([FromQuery] string? from, [FromQuery] string? to, [FromQuery] int? personId) =>
            Enabled(c => _attendance.GetReportAsync(c, from, to, personId));

        [HttpGet("api/attendance/my/employees")]
        public Task<IActionResult> Employees() =>
            Enabled(async c => AttendanceOutcome.Ok(await _attendance.GetEmployeesAsync(c.CustomerId)));

        [HttpGet("api/attendance/my/shifts")]
        public Task<IActionResult> Shifts() =>
            Enabled(async c => AttendanceOutcome.Ok(await _attendance.GetShiftsAsync(c.CustomerId)));

        [HttpPost("api/attendance/my/shifts")]
        public Task<IActionResult> AddShift([FromBody] AttendanceShiftInput? input) =>
            Enabled(c => _attendance.SaveShiftAsync(c.CustomerId, null, input, User.GetEmail()));

        [HttpPut("api/attendance/my/shifts/{id:int}")]
        public Task<IActionResult> UpdateShift(int id, [FromBody] AttendanceShiftInput? input) =>
            Enabled(c => _attendance.SaveShiftAsync(c.CustomerId, id, input, User.GetEmail()));

        /// <summary>Deletes a shift; its employees fall back to the default shift (or no shift).</summary>
        [HttpDelete("api/attendance/my/shifts/{id:int}")]
        public Task<IActionResult> DeleteShift(int id) =>
            Enabled(c => _attendance.DeleteShiftAsync(c.CustomerId, id, User.GetEmail()));

        /// <summary>An employee's own shift; shiftId null = the default shift (or no shift).</summary>
        [HttpPut("api/attendance/my/employees/{personId:int}/shift")]
        public Task<IActionResult> SetEmployeeShift(int personId, [FromBody] AttendanceShiftAssignInput? input) =>
            Enabled(c => _attendance.SetEmployeeShiftAsync(c.CustomerId, personId, input?.ShiftId, User.GetEmail()));

        /// <summary>Holidays of a year; without year all of them.</summary>
        [HttpGet("api/attendance/my/holidays")]
        public Task<IActionResult> Holidays([FromQuery] int? year) =>
            Enabled(async c => AttendanceOutcome.Ok(await _attendance.GetHolidaysAsync(c.CustomerId, year)));

        [HttpPost("api/attendance/my/holidays")]
        public Task<IActionResult> AddHoliday([FromBody] AttendanceHolidayInput? input) =>
            Enabled(c => _attendance.AddHolidayAsync(c.CustomerId, input, User.GetEmail()));

        [HttpDelete("api/attendance/my/holidays/{id:int}")]
        public Task<IActionResult> DeleteHoliday(int id) =>
            Enabled(c => _attendance.DeleteHolidayAsync(c.CustomerId, id));

        // ---- helpers ----

        private async Task<AttendanceCustomerRow?> MyCustomerAsync()
        {
            var userId = await _employees.ResolveUserIdAsync(User.GetEmail(), User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            return userId is > 0 ? await _attendance.GetCustomerByUserAsync(userId.Value) : null;
        }

        /// <summary>Runs the action for the signed-in customer when attendance is on for them; otherwise 403.</summary>
        private Task<IActionResult> Enabled(Func<AttendanceCustomerRow, Task<AttendanceOutcome>> action) =>
            Guarded(async () =>
            {
                var customer = await MyCustomerAsync();
                if (customer is not { Enabled: true })
                    return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail(NotEnabledMessage));
                return ToResult(await action(customer));
            });

        /// <summary>ATT_01 not run yet: 503 with the contract's message.</summary>
        internal static async Task<IActionResult> Guarded(Func<Task<IActionResult>> action)
        {
            try
            {
                return await action();
            }
            catch (AttendanceNotInstalledException)
            {
                return new ObjectResult(ApiResponse.Fail(AttendanceNotInstalledException.UserMessage)) { StatusCode = StatusCodes.Status503ServiceUnavailable };
            }
        }

        internal static IActionResult ToResult(AttendanceOutcome o) =>
            new ObjectResult(new ApiResponse { IsSuccessful = o.IsSuccessful, Message = o.Message, Data = o.Data }) { StatusCode = o.StatusCode };
    }
}
