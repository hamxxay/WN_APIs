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
    [Route("api/lease-templates")]
    [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,receptionist,Receptionist,sales_executive,SalesExecutive")] // staff only
    public class LeaseTemplatesController : ControllerBase
    {
        private readonly ILeaseTemplateService _templateService;

        public LeaseTemplatesController(ILeaseTemplateService templateService)
        {
            _templateService = templateService;
        }

        private int? ResolveActorId()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("id")?.Value
                       ?? User.FindFirst("sub")?.Value;

            if (int.TryParse(idClaim, out var id)) return id;
            return null;
        }

        [HttpGet("active")]
        public async Task<IActionResult> GetActiveTemplate([FromQuery] string name = "StandardLeaseAgreement")
        {
            try
            {
                var template = await _templateService.GetActiveTemplateAsync(name);
                if (template == null)
                {
                    return NotFound(new { isSuccessful = false, message = $"Active lease template '{name}' not found." });
                }

                return Ok(new
                {
                    isSuccessful = true,
                    data = template
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")] // publishing a new template version changes configuration: admin only
        public async Task<IActionResult> PublishTemplate([FromBody] PublishLeaseTemplateRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ContentHtml))
                {
                    return BadRequest(new { isSuccessful = false, message = "Template content HTML is required." });
                }

                int? actorId = ResolveActorId();
                var result = await _templateService.PublishTemplateAsync(request, actorId);

                return Ok(new
                {
                    isSuccessful = true,
                    data = result,
                    message = "Lease agreement template published successfully as new active version."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }
    }
}
