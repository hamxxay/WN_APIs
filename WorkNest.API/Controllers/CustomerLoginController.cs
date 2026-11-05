using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Temporary passwords for customer logins (script AUTH_TEMP_01_customer_temp_passwords; 503 before it has run).
    /// The password is in the response (shown once to staff) and in the customer's email, never in a log.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
    public class CustomerLoginController : ControllerBase
    {
        private readonly ICustomerLoginService _logins;
        public CustomerLoginController(ICustomerLoginService logins) => _logins = logins;

        /// <summary>
        /// New temporary password for one customer (creates the login user if missing), e-mailed to them. 200 data
        /// { customerId, email, tempPassword, emailed }; 400 "This customer has no email."; 404 for a customer of other
        /// locations (location-bound admins, RecordScope) or an unknown id.
        /// </summary>
        [HttpPost("api/admin/customers/{customerId:int}/temp-password")]
        [RecordScope(RecordKind.Customer, "customerId")]
        public async Task<IActionResult> Issue(int customerId)
        {
            // A customer with no booking has no location, so RecordScope lets every location through: location-bound
            // staff may create their first login, but resetting an existing one (a takeover) needs a super admin.
            if (User.IsLocationBoundRole() && await _logins.HasLoginWithoutLocationAsync(customerId, HttpContext.RequestAborted))
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("This customer has no booking at your location. Ask a super admin to reset the password."));
            var (status, response) = await _logins.IssueAsync(customerId, HttpContext.RequestAborted);
            return StatusCode(status, response);
        }

        /// <summary>
        /// Super admin only: a temporary password for every customer with an email whose login is not active. 200 data
        /// { created: [{ customerId, name, email, tempPassword, emailed }], skipped: [{ customerId, name, reason }] }.
        /// </summary>
        [HttpPost("api/admin/customers/temp-passwords/bulk")]
        [Authorize(Roles = "super_admin,SuperAdmin")]
        public async Task<IActionResult> Bulk()
        {
            var (status, response) = await _logins.IssueBulkAsync(HttpContext.RequestAborted);
            return StatusCode(status, response);
        }
    }
}
