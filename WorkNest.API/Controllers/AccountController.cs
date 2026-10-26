using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.API.Security;
using WorkNest.Application.DTOs.Account;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Delete your own account (Google Play rule, QA_04): the app (Profile > Delete account) and the website page
    /// worknestpk.com/delete-account. Only the signed-in user's own account, taken from the sign-in token (never from
    /// the request). Customers only: staff, CEO, Sales Manager and Access Officer accounts get "Ask a WorkNest admin to
    /// remove your account" (they may call these endpoints to read that answer). Refused (nothing deleted) while the
    /// customer has an active or pending booking or money due. Otherwise the procedure dbo.WN_Account_Delete switches
    /// the account off and removes its personal details in one transaction, keeping bookings, invoices, payments and
    /// agreements for accounting. Needs the database script QA_04_account_delete (before it: "not available yet", 503).
    /// </summary>
    [ApiController]
    [Authorize]
    public class AccountController : ControllerBase
    {
        private readonly IAccountDeletionService _deletion;
        private readonly TokenSessionValidator _sessions;

        public AccountController(IAccountDeletionService deletion, TokenSessionValidator sessions)
        {
            _deletion = deletion;
            _sessions = sessions;
        }

        /// <summary>
        /// { canDelete, reason, reasonCode, isCustomerAccount, activeBookings, pendingBookings, unpaidInvoices,
        /// balanceDue, needsDatabaseUpdate } in data, so the app / website can explain before asking. Changes nothing.
        /// </summary>
        [HttpGet("api/account/delete-check")]
        [CeoAllowed]
        [SalesManagerAllowed]
        [AccessOfficerAllowed]
        [NetworkEngineerAllowed]
        public async Task<IActionResult> DeleteCheck() =>
            Answer(await _deletion.CheckAsync(User.GetEmail(), UserIdClaim(), HttpContext.RequestAborted));

        /// <summary>
        /// { "confirm": "DELETE" } -> 200 { isSuccessful: true, message } when the account was deleted (this token and
        /// the account's other sign-ins stop working); 400 without the confirmation; 409 { message: reason, data: check }
        /// when refused (nothing deleted); 503 before QA_04; 500 when it failed (nothing changed).
        /// </summary>
        [HttpPost("api/account/delete")]
        [CeoAllowed]
        [SalesManagerAllowed]
        [AccessOfficerAllowed]
        [NetworkEngineerAllowed]
        public async Task<IActionResult> Delete([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AccountDeleteRequest? request)
        {
            var email = User.GetEmail();
            var outcome = await _deletion.DeleteAsync(email, UserIdClaim(), request?.Confirm, HttpContext.RequestAborted);
            if (outcome.Deleted)
            {
                // This token is refused from now on; the account's other tokens as soon as their account lookup is
                // refreshed (now: it is dropped from the cache), because the account is switched off.
                _sessions.RevokeCurrent(HttpContext);
                if (!string.IsNullOrWhiteSpace(email)) _sessions.ForgetAccount(email);
                return Ok(ApiResponse.Ok(new { deleted = true }, outcome.Message));
            }
            return Answer(outcome);
        }

        /// <summary>The token's user id (sub / NameIdentifier), checked against the account of its email.</summary>
        private string? UserIdClaim() =>
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        private IActionResult Answer(AccountDeletionOutcome outcome)
        {
            if (outcome.IsSuccessful) return StatusCode(outcome.StatusCode, ApiResponse.Ok(outcome.Check, outcome.Message));
            var body = ApiResponse.Fail(outcome.Message);
            body.Data = outcome.Check;
            return StatusCode(outcome.StatusCode, body);
        }
    }
}
