using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Filters;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// CMS "Employee history" of a booking: who was added / removed, fingerprints / faces / cards enrolled, door access
    /// switched, e-mail changed, and by whom. Read-only. Staff only; location-bound staff (admin, sales executive, sales
    /// manager, access officer) only see bookings of their own locations (404 otherwise, RecordScope Booking).
    /// </summary>
    [ApiController]
    [Authorize(Roles = Roles)]
    [ValidateLocationScope]
    [RecordScope(RecordKind.Booking, "bookingId")]
    [SalesManagerAllowed]
    [AccessOfficerAllowed]
    public class BookingEmployeeHistoryController : ControllerBase
    {
        public const string Roles = "super_admin,SuperAdmin,admin,Admin,sales_executive,SalesExecutive,sales_manager,SalesManager,access_officer,AccessOfficer";

        private readonly IBookingEmployeeService _employees;

        public BookingEmployeeHistoryController(IBookingEmployeeService employees) => _employees = employees;

        /// <summary>Newest first: { at, personId, personName, idNumberMasked, action, by, detail }.</summary>
        [HttpGet("api/booking/{bookingId:int}/employee-history")]
        public async Task<IActionResult> Get(int bookingId) =>
            Ok(new ApiResponse { IsSuccessful = true, Message = "Success", Data = await _employees.GetHistoryAsync(bookingId) });
    }
}
