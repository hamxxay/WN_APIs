using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.Application.DTOs.SecurityDeposit;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Adjust Security Deposit Against Invoice. A sales executive (or admin) requests; an admin / super admin approves,
    /// and approval posts everything in one SQL transaction (see SDA_01). Locations follow the user's LocationId(s).
    /// </summary>
    [ApiController]
    [Route("api/security-deposit-adjustments")]
    [Authorize(Roles = RequestRoles)]
    public class SecurityDepositAdjustmentController : ControllerBase
    {
        private const string RequestRoles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive";
        private const string ApproveRoles = "admin,Admin,super_admin,SuperAdmin";

        private readonly ISecurityDepositAdjustmentRepository _repo;
        private readonly IDbRepository _db;

        public SecurityDepositAdjustmentController(ISecurityDepositAdjustmentRepository repo, IDbRepository db)
        {
            _repo = repo;
            _db = db;
        }

        private async Task<int?> GetCurrentUserIdAsync()
        {
            var email = User.GetEmail();
            if (!string.IsNullOrWhiteSpace(email))
            {
                var (id, _) = await _db.GetUserIdByEmailAsync(email);
                if (id is > 0) return id;
            }
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("id")?.Value;
            return int.TryParse(idClaim, out var n) && n > 0 ? n : null;
        }

        /// <summary>Comma-separated location ids the caller may touch; null = all locations (super admin).</summary>
        private string? AllowedLocations()
        {
            if (User.IsSuperAdmin()) return null;
            if (User.IsLocationBoundRole())
            {
                var ids = User.GetLocationIds();
                return ids.Count > 0 ? string.Join(",", ids) : "-1";
            }
            return null;
        }

        private async Task<IActionResult> RunAsync(Func<Task<List<SdaResultSet>>> work, Func<List<SdaResultSet>, object?> shape, int successStatus = 200)
        {
            try
            {
                var sets = await work();
                return StatusCode(successStatus, ApiResponse.Ok(shape(sets)));
            }
            catch (SecurityDepositAdjustmentException ex)
            {
                var body = ApiResponse.Fail(ex.Message);
                return ex.Outcome switch
                {
                    SdaOutcome.NotFound => NotFound(body),
                    SdaOutcome.Forbidden => StatusCode(403, body),
                    _ => BadRequest(body)
                };
            }
        }

        /// <summary>Form data for a booking: deposit received / adjusted / available, the unpaid invoices that can be paid from it, history, requests.</summary>
        [HttpGet("booking/{bookingId:int}")]
        public Task<IActionResult> Context(int bookingId) =>
            RunAsync(() => _repo.GetContextAsync(bookingId, AllowedLocations()), s => new
            {
                summary = s[0].FirstOrDefault(),
                invoices = s[1],
                movements = s[2],
                requests = s[3]
            });

        /// <summary>Requests (optionally one booking / status); pending ones are what an admin approves.</summary>
        [HttpGet]
        public Task<IActionResult> List([FromQuery] int? bookingId, [FromQuery] string? status) =>
            RunAsync(() => _repo.ListAsync(bookingId, status, AllowedLocations()), s => s[0]);

        /// <summary>Request an adjustment. Posts nothing until an admin approves.</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SecurityDepositAdjustmentCreateDto request)
        {
            if (request == null) return BadRequest(ApiResponse.Fail("Invalid request body."));
            var userId = await GetCurrentUserIdAsync();
            if (userId == null) return Unauthorized(ApiResponse.Fail("Your login could not be matched to a user account. Please sign in again."));
            return await RunAsync(() => _repo.CreateAsync(request, userId.Value, AllowedLocations()), s => s[0].FirstOrDefault(), 201);
        }

        /// <summary>Approve (posts: deposit movement, invoice Paid, GL, top-up invoice, audit) or reject. Admin / super admin only.</summary>
        [HttpPost("{id:int}/decision")]
        [Authorize(Roles = ApproveRoles)]
        public async Task<IActionResult> Decide(int id, [FromBody] SecurityDepositAdjustmentDecisionDto request)
        {
            if (request == null) return BadRequest(ApiResponse.Fail("Invalid request body."));
            var userId = await GetCurrentUserIdAsync();
            if (userId == null) return Unauthorized(ApiResponse.Fail("Your login could not be matched to a user account. Please sign in again."));
            return await RunAsync(() => _repo.DecideAsync(id, request.Approve, request.Note, userId.Value, AllowedLocations()), s => s[0].FirstOrDefault());
        }

        /// <summary>Notice settlement: the deposit to settle against after adjustments (use depositHeld in the settlement dialog) and unpaid top-ups.</summary>
        [HttpGet("notice/{noticeId:int}/deposit")]
        [Authorize(Roles = ApproveRoles)]
        public Task<IActionResult> NoticeDeposit(int noticeId) =>
            RunAsync(() => _repo.GetNoticeDepositAsync(noticeId), s => new { summary = s[0].FirstOrDefault(), unpaidTopUps = s[1] });
    }
}
