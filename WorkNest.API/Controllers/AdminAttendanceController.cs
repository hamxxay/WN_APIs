using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.Attendance;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Staff view of customer attendance (CMS customer detail > Attendance, Reports > Attendance): read-only, also for
    /// customers with attendance off. Only admins / super admins turn attendance on or off. Location-bound staff see
    /// only customers of their own locations (others answer 404). Before the ATT_01 database update: 503.
    /// </summary>
    [ApiController]
    [Authorize(Roles = StaffRoles)]
    [CeoAllowed]
    [SalesManagerAllowed]
    [RecordScope(RecordKind.Customer, "customerId")] // location-bound staff: only customers of their locations
    public class AdminAttendanceController : ControllerBase
    {
        private const string StaffRoles =
            "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive,sales_manager,ceo";
        // Who may switch a customer's attendance on/off (location-bound staff only for their locations' customers).
        private const string AdminRoles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive";

        private readonly IAttendanceService _attendance;

        public AdminAttendanceController(IAttendanceService attendance)
        {
            _attendance = attendance;
        }

        /// <summary>Customers with attendance on or with employees on a room (attendance on first), [{ customerId, customerName, enabled, employeeCount }].</summary>
        [HttpGet("api/admin/attendance/customers")]
        public Task<IActionResult> Customers() =>
            AttendanceController.Guarded(async () =>
                Ok(ApiResponse.Ok(await _attendance.GetCustomersAsync(User.ScopedLocationIds(null)))));

        /// <summary>Turns attendance on / off for the customer; returns the customer's AttendanceStatus.</summary>
        [HttpPut("api/admin/customers/{customerId:int}/attendance")]
        [Authorize(Roles = AdminRoles)]
        public Task<IActionResult> SetEnabled(int customerId, [FromBody] AttendanceEnableInput? input) =>
            AttendanceController.Guarded(async () =>
            {
                if (input == null) return BadRequest(ApiResponse.Fail("enabled is required."));
                return AttendanceController.ToResult(await _attendance.SetEnabledAsync(customerId, input.Enabled, User.GetEmail()));
            });

        [HttpGet("api/admin/customers/{customerId:int}/attendance/status")]
        public Task<IActionResult> Status(int customerId) =>
            ForCustomer(customerId, c => Task.FromResult(AttendanceOutcome.Ok(
                new AttendanceStatusDto { Enabled = c.Enabled, CustomerId = c.CustomerId, CustomerName = c.CustomerName })));

        /// <summary>The customer's daily report (also when attendance is off), same parameters as the customer's own.</summary>
        [HttpGet("api/admin/attendance/report")]
        [RecordScope(RecordKind.Customer, "customerId", FromQuery = true)]
        public Task<IActionResult> Report([FromQuery] int customerId, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int? personId) =>
            ForCustomer(customerId, c => _attendance.GetReportAsync(c, from, to, personId));

        [HttpGet("api/admin/customers/{customerId:int}/attendance/shifts")]
        public Task<IActionResult> Shifts(int customerId) =>
            ForCustomer(customerId, async c => AttendanceOutcome.Ok(await _attendance.GetShiftsAsync(c.CustomerId)));

        /// <summary>Holidays of a year; without year all of them.</summary>
        [HttpGet("api/admin/customers/{customerId:int}/attendance/holidays")]
        public Task<IActionResult> Holidays(int customerId, [FromQuery] int? year) =>
            ForCustomer(customerId, async c => AttendanceOutcome.Ok(await _attendance.GetHolidaysAsync(c.CustomerId, year)));

        [HttpGet("api/admin/customers/{customerId:int}/attendance/employees")]
        public Task<IActionResult> Employees(int customerId) =>
            ForCustomer(customerId, async c => AttendanceOutcome.Ok(await _attendance.GetEmployeesAsync(c.CustomerId)));

        private Task<IActionResult> ForCustomer(int customerId, Func<AttendanceCustomerRow, Task<AttendanceOutcome>> action) =>
            AttendanceController.Guarded(async () =>
            {
                var customer = customerId > 0 ? await _attendance.GetCustomerAsync(customerId) : null;
                if (customer == null) return NotFound(ApiResponse.Fail("Customer not found."));
                return AttendanceController.ToResult(await action(customer));
            });
    }
}
