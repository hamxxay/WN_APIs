using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.Application.DTOs.NoticePeriod;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]
    [Route("api/staff/notice-periods")]
    public class StaffNoticeController : ControllerBase
    {
        private readonly INoticePeriodService _noticeService;
        private readonly IDbRepository _db;

        public StaffNoticeController(INoticePeriodService noticeService, IDbRepository db)
        {
            _noticeService = noticeService;
            _db = db;
        }

        private async Task<int?> ResolveCurrentUserIdAsync()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? User.FindFirst("UserId")?.Value
                       ?? User.FindFirst("id")?.Value;

            if (!string.IsNullOrWhiteSpace(idClaim) && int.TryParse(idClaim, out var intId))
            {
                return intId;
            }

            var email = User.FindFirst(ClaimTypes.Email)?.Value
                     ?? User.FindFirst("email")?.Value
                     ?? User.Identity?.Name;

            if (!string.IsNullOrWhiteSpace(email))
            {
                var userRow = await _db.GetUserByEmailAsync(email);
                if (userRow != null && userRow.TryGetValue("Id", out var uid) && uid != null)
                {
                    return Convert.ToInt32(uid);
                }
            }

            return null;
        }

        /// <summary>
        /// Staff view notice periods list (Pending requests, Issued, etc.).
        /// Super admin sees all; admin and sales_executive see only their assigned location(s).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetNoticeList([FromQuery] int? locationId = null, [FromQuery] string? status = null)
        {
            string? allowedLocationIds = null;
            if (User.IsLocationBoundRole())
            {
                var myLocations = User.GetLocationIds();
                if (myLocations.Count == 0)
                {
                    return Ok(ApiResponse.Ok(new System.Collections.Generic.List<NoticePeriodItemDto>()));
                }
                if (locationId.HasValue && !myLocations.Contains(locationId.Value))
                {
                    return Forbid();
                }
                allowedLocationIds = string.Join(",", myLocations);
            }

            var result = await _noticeService.GetStaffNoticesAsync(locationId, status, allowedLocationIds);
            return Ok(result);
        }

        /// <summary>
        /// Issue notice period (from an existing Requested notice or directly by staff).
        /// NoticeDate is editable, ExitDate is calculated from NoticeDate + NoticeDays.
        /// </summary>
        [HttpPost("issue")]
        public async Task<IActionResult> IssueNotice([FromBody] StaffIssueNoticeRequest request)
        {
            var staffId = await ResolveCurrentUserIdAsync();
            if (!staffId.HasValue) return Unauthorized(ApiResponse.Fail("Staff identity could not be verified."));

            if (request == null) return BadRequest(ApiResponse.Fail("Invalid request body."));

            var result = await _noticeService.IssueNoticeAsync(request, staffId.Value);
            if (!result.IsSuccessful) return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Decline a pending customer notice request.
        /// </summary>
        [HttpPost("{noticeId:int}/decline")]
        public async Task<IActionResult> DeclineNotice(int noticeId, [FromBody] StaffDeclineNoticeRequest request)
        {
            var staffId = await ResolveCurrentUserIdAsync();
            if (!staffId.HasValue) return Unauthorized(ApiResponse.Fail("Staff identity could not be verified."));

            if (noticeId <= 0) return BadRequest(ApiResponse.Fail("Invalid notice ID."));
            if (request == null || string.IsNullOrWhiteSpace(request.DeclineReason))
                return BadRequest(ApiResponse.Fail("Decline reason is required."));

            var result = await _noticeService.DeclineNoticeAsync(noticeId, request.DeclineReason, staffId.Value);
            if (!result.IsSuccessful) return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Update NoticeDate on an issued notice (recalculates ExitDate automatically).
        /// </summary>
        [HttpPatch("{noticeId:int}/notice-date")]
        public async Task<IActionResult> UpdateNoticeDate(int noticeId, [FromBody] StaffUpdateNoticeDateRequest request)
        {
            var staffId = await ResolveCurrentUserIdAsync();
            if (!staffId.HasValue) return Unauthorized(ApiResponse.Fail("Staff identity could not be verified."));

            if (noticeId <= 0) return BadRequest(ApiResponse.Fail("Invalid notice ID."));
            if (request == null) return BadRequest(ApiResponse.Fail("Invalid request body."));

            var result = await _noticeService.UpdateNoticeDateAsync(noticeId, request, staffId.Value);
            if (!result.IsSuccessful) return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Settlement Pre-Check: Checks if ExitDate has passed, retrieves deposit held,
        /// any unpaid invoices, and any advance-paid rent after ExitDate.
        /// Also retrieves CashInHand / Bank payout accounts.
        /// </summary>
        [HttpGet("{noticeId:int}/settlement-precheck")]
        public async Task<IActionResult> GetSettlementPreCheck(int noticeId)
        {
            if (noticeId <= 0) return BadRequest(ApiResponse.Fail("Invalid notice ID."));

            var result = await _noticeService.GetSettlementPreCheckAsync(noticeId);
            if (!result.IsSuccessful) return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Complete notice settlement (allowed only after ExitDate, one per notice):
        /// Posts GL JV (Dr SecurityReceived = deposit; Cr payout = refund; Cr Other Income = deductions).
        /// If deductions exceed deposit, raises a shortfall invoice and posts Dr AR, Cr Other Income.
        /// </summary>
        [HttpPost("settle")]
        public async Task<IActionResult> SaveSettlement([FromBody] SaveNoticeSettlementRequest request)
        {
            var staffId = await ResolveCurrentUserIdAsync();
            if (!staffId.HasValue) return Unauthorized(ApiResponse.Fail("Staff identity could not be verified."));

            if (request == null) return BadRequest(ApiResponse.Fail("Invalid request body."));

            var result = await _noticeService.SaveSettlementAsync(request, staffId.Value);
            if (!result.IsSuccessful) return BadRequest(result);

            return Ok(result);
        }
    }
}

