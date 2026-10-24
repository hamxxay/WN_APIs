using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Customer;
using WorkNest.Application.DTOs.Lists;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.API.Filters;
using WorkNest.API.Extensions;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")] // customer records: staff only
    [RecordScope(RecordKind.Customer, "id")] // location-bound staff: only records of their locations
    public class CustomerController : ControllerBase
    {
        private readonly ICustomerService _customers;
        public CustomerController(ICustomerService customers) => _customers = customers;

        private const string AdminRoles = "admin,Admin,super_admin,SuperAdmin";

        // locationId (customers of that location: their user account or a booking there), status (1 active, 0 inactive,
        // from api/customer/statuses), from / to (created date, both included): only then the LST_01 procedure is used,
        // and the answer also carries total (for paging); without them the list is read exactly as before.
        // Location-bound staff always get the LST_01 list limited to their locations (customers of their locations:
        // their user account or a booking there), with or without a filter.
        [HttpGet("api/customer")]
        public async Task<IActionResult> List(
            [FromServices] IListFilterService lists,
            [FromQuery] int page = 1,
            [FromQuery] int limit = 20,
            [FromQuery] string search = "",
            [FromQuery] int? locationId = null,
            [FromQuery] string? status = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            var filter = new ListFilter { Status = status, From = from, To = to };
            if (!filter.HasStatusOrDates && locationId is not > 0 && !User.IsLocationBoundRole())
                return Ok(await _customers.GetAllCustomersAsync(page, limit, search));

            return await ListFilterResults.RunAsync(async () =>
            {
                // A location narrows the list (location-bound staff: only to one of theirs, otherwise all of theirs);
                // without one everyone else sees every customer, as the unfiltered list does.
                var locations = User.ScopedLocationIds(locationId);
                var (rows, total) = await lists.GetCustomersAsync(page, limit, search, locations, filter);
                return Ok(new { isSuccessful = true, message = "Success", data = rows, total = total });
            });
        }

        /// <summary>Choices of the Customers list Status filter (active / inactive), [{ value, label }].</summary>
        [HttpGet("api/customer/statuses")]
        public Task<IActionResult> ListStatuses([FromServices] IListFilterService lists) =>
            ListFilterResults.StatusOptionsAsync(lists, ListFilterRules.Customers);

        /// <summary>
        /// Customer picker search (top 20 active). Location-bound staff: the customers of their locations plus the
        /// customers with no location yet (no located account, no booking; open to every branch, as the customer record
        /// is), via SEC_LOC_02 dbo.WN_Customers_SearchScoped (503 until that script has run).
        /// </summary>
        [HttpGet("api/customer/search")]
        public async Task<IActionResult> Search([FromQuery] string q, [FromServices] IDbRepository db)
        {
            try
            {
                var rows = await db.SearchCustomersScopedAsync(q ?? "", LocationScopeLists.Csv(User.ScopedLocationIds(null)));
                return Ok(WorkNest.Common.Responses.ApiResponse.Ok(rows));
            }
            catch (LocationScopeUpdateMissingException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, WorkNest.Common.Responses.ApiResponse.Fail(ex.Message));
            }
        }

        [HttpGet("api/customer/{id}")]
        public async Task<IActionResult> GetById(string id) =>
            Ok(await _customers.GetCustomerByIdAsync(id));

        /// <summary>
        /// Customer detail page (CMS /admin/customers/:id): summary figures, bookings, fee receipts, payments, agreements,
        /// security deposits and an activity timeline (quotations and invoices have their own endpoints). Location-bound
        /// staff get 404 for a customer of other locations (RecordScope on the class); 503 until CD_01 has run.
        /// </summary>
        [HttpGet("api/customer/{id:int}/overview")]
        public async Task<IActionResult> Overview(int id)
        {
            var result = await _customers.GetCustomerOverviewAsync(id);
            return StatusCode(result.StatusCode, result.Response);
        }

        [HttpPost("api/customer")]
        public async Task<IActionResult> Create([FromBody] CustomerRequest request) =>
            StatusCode(201, await _customers.CreateCustomerAsync(request, User.GetEmail()));

        [HttpPut("api/customer/{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] CustomerRequest request) =>
            Ok(await _customers.UpdateCustomerAsync(id, request));

        [HttpDelete("api/customer/{id}")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> Delete(string id) =>
            Ok(await _customers.DeleteCustomerAsync(id));
    }
}
