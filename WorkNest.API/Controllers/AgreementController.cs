using System;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WorkNest.Application.DTOs.Agreement;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Configurations;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Route("api/agreements")]
    [Authorize]
    public class AgreementController : ControllerBase
    {
        private readonly IAgreementService _agreement;
        private readonly IWebHostEnvironment _env;
        private readonly FileStorageSettings _storageSettings;

        public AgreementController(
            IAgreementService agreement,
            IWebHostEnvironment env,
            IOptions<FileStorageSettings> storageSettings)
        {
            _agreement = agreement;
            _env = env;
            _storageSettings = storageSettings.Value;
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

        [HttpPost("{id:int}/upload-signed")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> UploadSignedAgreement(int id, IFormFile? file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { isSuccessful = false, message = "No file uploaded or file is empty." });
                }

                // 1. Validate file extension
                var ext = Path.GetExtension(file.FileName);
                if (string.IsNullOrWhiteSpace(ext) || !ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { isSuccessful = false, message = "Only PDF files (.pdf) are allowed." });
                }

                // 2. Validate max file size (10 MB)
                const long maxSizeBytes = 10 * 1024 * 1024;
                if (file.Length > maxSizeBytes)
                {
                    return BadRequest(new { isSuccessful = false, message = "File size exceeds the 10MB limit." });
                }

                // 3. Validate PDF magic bytes (%PDF-)
                await using (var stream = file.OpenReadStream())
                {
                    var header = new byte[5];
                    int read = await stream.ReadAsync(header, 0, 5);
                    if (read < 5 || Encoding.ASCII.GetString(header) != "%PDF-")
                    {
                        return BadRequest(new { isSuccessful = false, message = "Invalid file signature. Uploaded file is not a valid PDF document." });
                    }
                }

                // 4. Verify Agreement exists
                var agreement = await _agreement.GetAgreementByIdAsync(id);
                if (agreement == null)
                {
                    return NotFound(new { isSuccessful = false, message = $"Agreement #{id} not found." });
                }

                // 5. Resolve storage path
                var configPath = _storageSettings.SignedAgreementsPath;
                if (string.IsNullOrWhiteSpace(configPath))
                    configPath = "App_Data/SignedAgreements";

                var targetDir = Path.IsPathRooted(configPath)
                    ? configPath
                    : Path.Combine(_env.ContentRootPath, configPath);

                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                var fileName = $"{id}_{Guid.NewGuid():N}.pdf";
                var fullFilePath = Path.Combine(targetDir, fileName);

                await using (var destStream = new FileStream(fullFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await file.CopyToAsync(destStream);
                }

                var uploadedAtUtc = DateTime.UtcNow;
                await _agreement.UpdateSignedPdfInfoAsync(id, fullFilePath, uploadedAtUtc);

                return Ok(new
                {
                    isSuccessful = true,
                    data = new
                    {
                        agreementId = id,
                        signedPdfUploadedAt = uploadedAtUtc,
                        fileName = fileName
                    },
                    message = "Signed agreement uploaded successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [HttpGet("{id:int}/signed-pdf")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
        public async Task<IActionResult> DownloadSignedAgreementPdf(int id)
        {
            try
            {
                var agreement = await _agreement.GetAgreementByIdAsync(id);
                if (agreement == null)
                {
                    return NotFound(new { isSuccessful = false, message = $"Agreement #{id} not found." });
                }

                if (string.IsNullOrWhiteSpace(agreement.SignedPdfPath))
                {
                    return NotFound(new { isSuccessful = false, message = $"No signed PDF has been uploaded for Agreement #{id}." });
                }

                var fullFilePath = agreement.SignedPdfPath;
                if (!Path.IsPathRooted(fullFilePath))
                {
                    fullFilePath = Path.Combine(_env.ContentRootPath, fullFilePath);
                }

                if (!System.IO.File.Exists(fullFilePath))
                {
                    return NotFound(new { isSuccessful = false, message = "Signed PDF file is missing on disk." });
                }

                var fileBytes = await System.IO.File.ReadAllBytesAsync(fullFilePath);
                return File(fileBytes, "application/pdf", $"signed-lease-{id}.pdf");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
        public async Task<IActionResult> DeleteAgreement(int id)
        {
            try
            {
                var success = await _agreement.DeleteAgreementAsync(id);
                if (!success)
                {
                    return NotFound(new { isSuccessful = false, message = $"Agreement #{id} not found." });
                }

                return Ok(new { isSuccessful = true, message = $"Agreement #{id} deleted successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        [HttpDelete("{id:int}/signed-pdf")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin")]
        public async Task<IActionResult> DeleteSignedPdf(int id)
        {
            try
            {
                var success = await _agreement.DeleteSignedPdfAsync(id);
                if (!success)
                {
                    return NotFound(new { isSuccessful = false, message = $"Agreement #{id} not found." });
                }

                return Ok(new { isSuccessful = true, message = $"Signed copy PDF for Agreement #{id} deleted successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }
    }
}
