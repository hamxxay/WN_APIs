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
            if (!filter.HasStatusOrDates && locationId is not > 0)
                return Ok(await _customers.GetAllCustomersAsync(page, limit, search));

            return await ListFilterResults.RunAsync(async () =>
            {
                // A location narrows the list (location-bound staff: only to one of theirs); without one every customer
                // is listed, as the unfiltered list does.
                var locations = locationId is > 0 ? User.ScopedLocationIds(locationId) : null;
                var (rows, total) = await lists.GetCustomersAsync(page, limit, search, locations, filter);
                return Ok(new { isSuccessful = true, message = "Success", data = rows, total = total });
            });
        }

        /// <summary>Choices of the Customers list Status filter (active / inactive), [{ value, label }].</summary>
        [HttpGet("api/customer/statuses")]
        public Task<IActionResult> ListStatuses([FromServices] IListFilterService lists) =>
            ListFilterResults.StatusOptionsAsync(lists, ListFilterRules.Customers);

        [HttpGet("api/customer/search")]
        public async Task<IActionResult> Search([FromQuery] string q) =>
            Ok(await _customers.SearchCustomersAsync(q ?? ""));

        [HttpGet("api/customer/{id}")]
        public async Task<IActionResult> GetById(string id) =>
            Ok(await _customers.GetCustomerByIdAsync(id));
        
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
