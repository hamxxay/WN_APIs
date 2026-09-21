using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Agreement;
using WorkNest.Application.Interfaces;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AgreementController : ControllerBase
    {
        private readonly IAgreementService _agreement;

        public AgreementController(IAgreementService agreement)
        {
            _agreement = agreement;
        }

        private int? ResolveActorId()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("id")?.Value
                       ?? User.FindFirst("sub")?.Value;

            if (int.TryParse(idClaim, out var id)) return id;
            return null;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendAgreement([FromBody] SendAgreementRequest request)
        {
            try
            {
                if (request == null || request.QuotationId <= 0)
                    return BadRequest(new { isSuccessful = false, message = "Valid QuotationId is required." });

                int? actorId = ResolveActorId();
                var result = await _agreement.SendAgreementAsync(request, actorId);

                return Ok(new
                {
                    isSuccessful = true,
                    data = result,
                    message = "Lease agreement generated and sent successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAgreements(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null,
            [FromQuery] string? status = null)
        {
            try
            {
                var (rows, total) = await _agreement.GetAgreementsListAsync(page, limit, search, status);
                return Ok(new
                {
                    isSuccessful = true,
                    data = rows,
                    totalCount = total,
                    page = page,
                    limit = limit
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [HttpGet("{id:int}/pdf")]
        [AllowAnonymous]
        public async Task<IActionResult> DownloadAgreementPdf(int id)
        {
            try
            {
                var pdfBytes = await _agreement.GetAgreementPdfAsync(id);
                return File(pdfBytes, "application/pdf", $"Agreement-{id}.pdf");
            }
            catch (Exception ex)
            {
                return NotFound(new { isSuccessful = false, message = ex.Message });
            }
        }

        [HttpPost("{id:int}/mark-signed")]
        public async Task<IActionResult> MarkSigned(int id, [FromBody] MarkAgreementSignedRequest? req)
        {
            try
            {
                int? actorId = ResolveActorId();
                var result = await _agreement.MarkAgreementSignedAsync(id, actorId, req?.Note);

                return Ok(new
                {
                    isSuccessful = true,
                    data = result,
                    message = "Agreement marked as Signed. Booking created and confirmed."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }
    }
}
