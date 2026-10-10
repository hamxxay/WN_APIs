using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Customer;
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

        [HttpGet("api/customer")]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 20,
            [FromQuery] string search = "") =>
            Ok(await _customers.GetAllCustomersAsync(page, limit, search));

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
