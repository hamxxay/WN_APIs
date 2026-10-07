using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.Application.DTOs.Kyc;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Constants;
using WorkNest.Domain.Entities;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Authorize(Policy = "KycAccessPolicy")]
    [Route("api/kyc")]
    public class KycController : ControllerBase
    {
        private readonly IKycService _kycService;

        public KycController(IKycService kycService)
        {
            _kycService = kycService;
        }

        private int GetCurrentUserId()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("id")?.Value
                       ?? User.FindFirst("user_id")?.Value;

            return int.TryParse(idClaim, out var id) ? id : 1;
        }

        private string GetCurrentUserRole() => User.GetRole();
        // Only Sales Executives are limited to their location; Admins and Super Admins see every location.
        private int? GetCurrentLocationId() => User.IsLocationBoundRole() ? User.GetLocationId() : null;
        private bool SeesAllLocations() => !User.IsLocationBoundRole();
        private bool CanVerify() => Roles.IsAdminRole(GetCurrentUserRole());

        /// <summary>
        /// KYC Portal - Customer List Page
        /// </summary>
        [HttpGet("")]
        public async Task<IActionResult> Index(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 20,
            [FromQuery] string? search = null)
        {
            var (items, total) = await _kycService.GetCustomerKycListAsync(page, limit, search, GetCurrentLocationId(), SeesAllLocations());
            return Ok(new { items, total, page, limit });
        }

        /// <summary>
        /// KYC Portal - Customer Details &amp; KYC Document Management Page
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> CustomerKyc(string id)
        {
            var model = await _kycService.GetCustomerKycPortalAsync(id, GetCurrentLocationId(), SeesAllLocations(), CanVerify());
            if (model == null)
            {
                return NotFound(new { message = "Customer not found or access is unauthorized for your location." });
            }

            return Ok(model);
        }

        /// <summary>
        /// Upload or Replace KYC Document
        /// </summary>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadDocument([FromForm] UploadKycDocumentDto request)
        {
            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest(new { success = false, message = "Please select a file to upload." });
            }

            var (success, message, docId) = await _kycService.UploadOrReplaceDocumentAsync(
                request.CustomerId,
                request.DocumentTypeId,
                request.SlotNo <= 0 ? (byte)1 : request.SlotNo,
                request.HolderName,
                request.ExpiryDate,
                request.File,
                GetCurrentUserId(),
                GetCurrentLocationId(),
                SeesAllLocations()
            );

            if (!success)
            {
                return BadRequest(new { success = false, message });
            }

            return Ok(new { success = true, message, documentId = docId });
        }

        /// <summary>
        /// Verify KYC Document (SuperAdmin and Admin Only)
        /// </summary>
        [HttpPost("verify")]
        [Authorize(Policy = "KycVerifyPolicy")]
        public async Task<IActionResult> VerifyDocument([FromBody] VerifyKycDocumentDto request)
        {
            var (success, message) = await _kycService.VerifyOrRejectDocumentAsync(
                request.DocumentId,
                KycDocumentStatus.Verified,
                request.Remarks,
                GetCurrentUserId(),
                GetCurrentUserRole(),
                GetCurrentLocationId(),
                SeesAllLocations()
            );

            if (!success)
            {
                return BadRequest(new { success = false, message });
            }

            return Ok(new { success = true, message });
        }

        /// <summary>
        /// Reject KYC Document (SuperAdmin and Admin Only)
        /// </summary>
        [HttpPost("reject")]
        [Authorize(Policy = "KycVerifyPolicy")]
        public async Task<IActionResult> RejectDocument([FromBody] VerifyKycDocumentDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Remarks))
            {
                return BadRequest(new { success = false, message = "Rejection remarks are mandatory." });
            }

            var (success, message) = await _kycService.VerifyOrRejectDocumentAsync(
                request.DocumentId,
                KycDocumentStatus.Rejected,
                request.Remarks,
                GetCurrentUserId(),
                GetCurrentUserRole(),
                GetCurrentLocationId(),
                SeesAllLocations()
            );

            if (!success)
            {
                return BadRequest(new { success = false, message });
            }

            return Ok(new { success = true, message });
        }

        /// <summary>
        /// View or Download Document File (Authorized & Location-Scoped Stream)
        /// </summary>
        [HttpGet("document/{id}/download")]
        [HttpGet("document/{id}/view")]
        public async Task<IActionResult> StreamDocument(int id, [FromQuery] bool inline = false)
        {
            var (stream, contentType, fileName, error) = await _kycService.DownloadDocumentAsync(
                id,
                GetCurrentLocationId(),
                SeesAllLocations() // anonymous visitors used to be treated as super admin here
            );

            if (stream == null)
            {
                return NotFound(new { message = error });
            }

            Response.Headers.Append("X-Content-Type-Options", "nosniff");
            Response.Headers.Append("Content-Disposition", $"{(inline ? "inline" : "attachment")}; filename=\"{fileName}\"");

            return File(stream, contentType);
        }

        /// <summary>
        /// Document Version History
        /// </summary>
        [HttpGet("history/{customerId}/{documentTypeId}/{slotNo}")]
        public async Task<IActionResult> GetDocumentHistory(int customerId, int documentTypeId, byte slotNo = 1)
        {
            var history = await _kycService.GetDocumentHistoryAsync(
                customerId,
                documentTypeId,
                slotNo,
                GetCurrentLocationId(),
                SeesAllLocations()
            );

            return Ok(history);
        }
    }
}
