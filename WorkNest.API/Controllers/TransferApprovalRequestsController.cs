using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.Transfer;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services.Transfers;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Space transfers a sales executive asked for (every one needs the sales manager). The sales manager (own locations), an admin
    /// (own locations) or a super admin recalculates, approves (the transfer runs with the final values) or sends back with a note.
    /// A sales executive lists / opens their own requests only.
    /// </summary>
    [ApiController]
    [Route("api/booking-transfers/approval-requests")]
    [Authorize(Roles = "sales_manager,SalesManager,super_admin,SuperAdmin,admin,Admin,sales_executive,SalesExecutive")]
    [SalesManagerAllowed]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class TransferApprovalRequestsController : ControllerBase
    {
        private readonly ITransferService _transfers;
        private readonly IDbRepository _db;

        public TransferApprovalRequestsController(ITransferService transfers, IDbRepository db)
        {
            _transfers = transfers;
            _db = db;
        }

        private async Task<TransferCaller> CallerAsync()
        {
            var email = User.GetEmail();
            var row = string.IsNullOrWhiteSpace(email) ? null : await _db.GetUserByEmailAsync(email);
            int? id = row != null && row.TryGetValue("Id", out var v) && v != null ? Convert.ToInt32(v) : null;
            return User.ToTransferCaller(id);
        }

        private IActionResult Answer(ApiResponse r) => r.IsSuccessful ? Ok(r) : BadRequest(r);

        /// <summary>?status=pending (default) | all.</summary>
        [HttpGet("")]
        public async Task<IActionResult> List([FromQuery] string? status = null) =>
            Answer(await _transfers.ListRequestsAsync(await CallerAsync(), !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase)));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            var r = await _transfers.GetRequestAsync(await CallerAsync(), id);
            return r.IsSuccessful ? Ok(r) : NotFound(r);
        }

        [HttpPost("{id:int}/recalculate")]
        public async Task<IActionResult> Recalculate(int id, [FromBody] TransferDecisionRequest? body)
        {
            var caller = await CallerAsync();
            if (!TransferAccess.CanDecide(caller)) return StatusCode(403, ApiResponse.Fail("Only the sales manager can change a request."));
            return Answer(await _transfers.RecalculateRequestAsync(caller, id, body));
        }

        [HttpPost("{id:int}/approve")]
        public async Task<IActionResult> Approve(int id, [FromBody] TransferDecisionRequest? body)
        {
            var caller = await CallerAsync();
            if (!TransferAccess.CanDecide(caller)) return StatusCode(403, ApiResponse.Fail("Only the sales manager can approve."));
            return Answer(await _transfers.ApproveRequestAsync(caller, id, body, User.GetEmail()));
        }

        [HttpPost("{id:int}/send-back")]
        public async Task<IActionResult> SendBack(int id, [FromBody] TransferSendBackRequest? body)
        {
            var caller = await CallerAsync();
            if (!TransferAccess.CanDecide(caller)) return StatusCode(403, ApiResponse.Fail("Only the sales manager can send it back."));
            return Answer(await _transfers.SendBackRequestAsync(caller, id, body?.Note));
        }
    }
}
