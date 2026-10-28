using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.Application.DTOs.Attendant;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// "Employees" on a customer's own booking (website My Bookings, app booking detail). Only the customer who booked
    /// it (any signed-in account, also a staff account that booked; WN_Bookings.UserId, or the booking is invoiced to
    /// their customer record) - anyone else, also
    /// an employee with their own login, gets 404. People they add wait for a CNIC check by sales at the branch, then
    /// they are the booking's attendants (CMS Attendants &amp; Access). Customers never enrol fingerprints / faces / cards.
    /// </summary>
    [ApiController]
    [Authorize] // ownership is checked per booking / customer, so a staff account that is also a customer works too
    public class CustomerEmployeeController : ControllerBase
    {
        private readonly IBookingEmployeeService _employees;

        public CustomerEmployeeController(IBookingEmployeeService employees)
        {
            _employees = employees;
        }

        /// <summary>The booking's employees (on the room + waiting for the CNIC check), seats, and the customer's other people.</summary>
        [HttpGet("api/booking/my/{bookingId:int}/employees")]
        public Task<IActionResult> Get(int bookingId) =>
            RunAsync(userId => _employees.GetAsync(bookingId, userId));

        /// <summary>Add an employee (mode "new" or "existing"); they wait for the CNIC check at the branch.</summary>
        [HttpPost("api/booking/my/{bookingId:int}/employees")]
        public Task<IActionResult> Add(int bookingId, [FromBody] AddBookingEmployeeRequest? request) =>
            RunAsync(userId => _employees.AddAsync(bookingId, userId, request));

        /// <summary>Remove an employee; one already on the room also loses their door access right away.</summary>
        [HttpDelete("api/booking/my/{bookingId:int}/employees/{personId:int}")]
        public Task<IActionResult> Remove(int bookingId, int personId) =>
            RunAsync(userId => _employees.RemoveAsync(bookingId, userId, personId));

        private async Task<IActionResult> RunAsync(Func<int, Task<BookingEmployeeOutcome>> action)
        {
            var userId = await _employees.ResolveUserIdAsync(User.GetEmail(), User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            if (userId is not > 0) return NotFound(ApiResponse.Fail("Booking not found."));
            try
            {
                return ToResult(await action(userId.Value));
            }
            catch (BookingEmployeesNotInstalledException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse.Fail(BookingEmployeesNotInstalledException.UserMessage));
            }
        }

        internal static IActionResult ToResult(BookingEmployeeOutcome o) =>
            new ObjectResult(new ApiResponse { IsSuccessful = o.IsSuccessful, Message = o.Message, Data = o.Data }) { StatusCode = o.StatusCode };
    }
}
