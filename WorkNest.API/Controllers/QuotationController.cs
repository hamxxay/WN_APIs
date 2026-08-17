using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Quotation;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize]
    public class QuotationController : ControllerBase
    {
        private readonly IQuotationService _quotations;
        private readonly IDbRepository _db;

        public QuotationController(IQuotationService quotations, IDbRepository db)
        {
            _quotations = quotations;
            _db = db;
        }

        private string? ResolveUserEmail(string? headerEmail)
        {
            var claimEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                             ?? User.FindFirst("email")?.Value;
            return !string.IsNullOrWhiteSpace(claimEmail) ? claimEmail : headerEmail;
        }

        [HttpPost("api/quotation")]
        [Authorize(Roles = "admin,super_admin,receptionist")]
        public async Task<IActionResult> Create([FromBody] QuotationRequest request, [FromHeader(Name = "x-user-email")] string? actorEmail)
        {
            var email = ResolveUserEmail(actorEmail);
            int? actorId = null;
            if (!string.IsNullOrWhiteSpace(email))
            {
                var actorRow = await _db.GetUserByEmailAsync(email);
                actorId = actorRow?.TryGetValue("Id", out var aid) == true ? System.Convert.ToInt32(aid) : (int?)null;
            }

            try
            {
                var res = await _quotations.CreateQuotationAsync(request, actorId);
                return Ok(ApiResponse.Ok(res, "Quotation generated successfully."));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        }

        [HttpGet("api/quotation/{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var res = await _quotations.GetQuotationByIdAsync(id);
            if (res == null) return NotFound(ApiResponse.Fail("Quotation not found."));
            return Ok(ApiResponse.Ok(res));
        }

        [HttpGet("api/quotation")]
        [Authorize(Roles = "admin,super_admin,receptionist")]
        public async Task<IActionResult> GetList([FromQuery] int page = 1, [FromQuery] int limit = 10, [FromQuery] string? search = null)
        {
            var (rows, total) = await _quotations.GetQuotationsAsync(page, limit, search);
            return Ok(new { data = rows, total = total, page = page, limit = limit });
        }

        [HttpGet("api/quotation/history")]
        [Authorize(Roles = "admin,super_admin,receptionist")]
        public async Task<IActionResult> GetHistory([FromQuery] int customerId, [FromQuery] int spaceId)
        {
            var res = await _quotations.GetQuotationHistoryAsync(customerId, spaceId);
            return Ok(ApiResponse.Ok(res));
        }

        [HttpPost("api/quotation/{id:int}/send-email")]
        [Authorize(Roles = "admin,super_admin,receptionist")]
        public async Task<IActionResult> SendEmail(int id, [FromBody] SendQuotationEmailRequest request)
        {
            try
            {
                await _quotations.SendQuotationEmailAsync(id, request.Email);
                return Ok(ApiResponse.Ok("Quotation emailed successfully."));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        }

        [HttpPost("api/quotation/{id:int}/convert")]
        [Authorize(Roles = "admin,super_admin,receptionist")]
        public async Task<IActionResult> ConvertQuotation(int id, [FromHeader(Name = "x-user-email")] string? actorEmail)
        {
            var email = ResolveUserEmail(actorEmail);
            int? actorId = null;
            if (!string.IsNullOrWhiteSpace(email))
            {
                var actorRow = await _db.GetUserByEmailAsync(email);
                actorId = actorRow?.TryGetValue("Id", out var aid) == true ? System.Convert.ToInt32(aid) : (int?)null;
            }

            try
            {
                var res = await _quotations.ConvertQuotationToBookingAsync(id, actorId);
                return Ok(ApiResponse.Ok(res, "Quotation converted to booking successfully."));
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse.Fail(ex.Message));
            }
        }
    }
}
