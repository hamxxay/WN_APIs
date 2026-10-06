using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.Application.DTOs.Agreement;
using WorkNest.Application.Interfaces;
using Microsoft.AspNetCore.RateLimiting;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Route("api/leases")]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")] // generating a lease: staff only
    public class LeasesController : ControllerBase
    {
        private readonly IAgreementService _agreementService;

        public LeasesController(IAgreementService agreementService)
        {
            _agreementService = agreementService;
        }

        private int? ResolveActorId()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("id")?.Value
                       ?? User.FindFirst("sub")?.Value;

            if (int.TryParse(idClaim, out var id)) return id;
            return null;
        }

        [EnableRateLimiting("pdf")]
        [HttpPost("generate")]
        public async Task<IActionResult> GenerateLease([FromBody] GenerateLeaseAgreementRequest request)
        {
            try
            {
                if (request == null || request.QuotationId <= 0)
                {
                    return BadRequest(new { isSuccessful = false, message = "Valid QuotationId is required." });
                }

                int? actorId = ResolveActorId();
                var (pdfBytes, agreementDto) = await _agreementService.GenerateLeaseAgreementAsync(request, actorId);

                // Set custom response headers for metadata while returning PDF stream
                Response.Headers.Append("X-Agreement-Id", agreementDto.Id.ToString());
                Response.Headers.Append("X-Template-Version-Id", agreementDto.TemplateVersionId?.ToString() ?? "");

                return File(pdfBytes, "application/pdf", $"LeaseAgreement-{agreementDto.Id}.pdf");
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { isSuccessful = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }
    }
}
