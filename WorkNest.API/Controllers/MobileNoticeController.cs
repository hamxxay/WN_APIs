using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.NoticePeriod;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/mobile/notice-periods")]
    public class MobileNoticeController : ControllerBase
    {
        private readonly INoticePeriodService _noticeService;
        private readonly IDbRepository _db;

        public MobileNoticeController(INoticePeriodService noticeService, IDbRepository db)
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
        /// Customer submits a leave request for a booking (intent only, no extra data needed).
        /// </summary>
        [HttpPost("submit")]
        public async Task<IActionResult> SubmitNotice([FromBody] SubmitNoticeRequest request)
        {
            var userId = await ResolveCurrentUserIdAsync();
            if (!userId.HasValue) return Unauthorized(ApiResponse.Fail("User identity could not be verified."));

            if (request == null || request.BookingId <= 0)
                return BadRequest(ApiResponse.Fail("Valid bookingId is required."));

            var result = await _noticeService.SubmitNoticeAsync(request.BookingId, userId.Value, request.Note);
            if (!result.IsSuccessful) return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Customer views their notice requests and statuses (Requested / Issued / Declined / Withdrawn / Completed).
        /// </summary>
        [HttpGet("my")]
        public async Task<IActionResult> GetMyNotices()
        {
            var userId = await ResolveCurrentUserIdAsync();
            if (!userId.HasValue) return Unauthorized(ApiResponse.Fail("User identity could not be verified."));

            var result = await _noticeService.GetCustomerNoticesAsync(userId.Value);
            return Ok(result);
        }

        /// <summary>
        /// Customer can withdraw a notice request while it is still in 'Requested' status.
        /// </summary>
        [HttpPatch("{noticeId:int}/withdraw")]
        public async Task<IActionResult> WithdrawNotice(int noticeId)
        {
            var userId = await ResolveCurrentUserIdAsync();
            if (!userId.HasValue) return Unauthorized(ApiResponse.Fail("User identity could not be verified."));

            if (noticeId <= 0) return BadRequest(ApiResponse.Fail("Invalid notice ID."));

            var result = await _noticeService.WithdrawNoticeAsync(noticeId, userId.Value);
            if (!result.IsSuccessful) return BadRequest(result);

            return Ok(result);
        }
    }
}

