using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Customer;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class CustomerController : ControllerBase
    {
        private readonly ICustomerService _customers;
        public CustomerController(ICustomerService customers) => _customers = customers;

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
        public async Task<IActionResult> Create(
            [FromBody] CustomerRequest request,
            [FromHeader(Name = "x-user-email")] string? userEmail) =>
            StatusCode(201, await _customers.CreateCustomerAsync(request, userEmail));

        [HttpPut("api/customer/{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] CustomerRequest request) =>
            Ok(await _customers.UpdateCustomerAsync(id, request));

        [HttpDelete("api/customer/{id}")]
        public async Task<IActionResult> Delete(string id) =>
            Ok(await _customers.DeleteCustomerAsync(id));
    }
}
