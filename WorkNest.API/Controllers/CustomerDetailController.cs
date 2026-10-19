using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Customer detail page (CMS /admin/customers/:id). Owner's rule (19/10/2026): the page shows every customer the
    /// Customers list (GET api/customer) shows, to the same roles, so these endpoints have the list's roles and no
    /// location check (CustomerController's RecordScope would hide customers the list shows). Quotations and invoices
    /// come from their existing endpoints, which keep their own checks.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")] // same roles as GET api/customer
    public class CustomerDetailController : ControllerBase
    {
        private readonly ICustomerService _customers;
        public CustomerDetailController(ICustomerService customers) => _customers = customers;

        /// <summary>The customer's profile (same fields and answer as GET api/customer/{id}).</summary>
        [HttpGet("api/customer/{id:int}/detail")]
        public async Task<IActionResult> Detail(int id) =>
            Ok(await _customers.GetCustomerByIdAsync(id.ToString()));

        /// <summary>
        /// Summary figures, bookings, fee receipts, payments, agreements, security deposits and an activity timeline.
        /// 404 for no such customer; 503 until the database script CD_01 has run.
        /// </summary>
        [HttpGet("api/customer/{id:int}/overview")]
        public async Task<IActionResult> Overview(int id)
        {
            var result = await _customers.GetCustomerOverviewAsync(id);
            return StatusCode(result.StatusCode, result.Response);
        }
    }
}
