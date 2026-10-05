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
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")] // staff only
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
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")] // all customers' agreements: staff only
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
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")] // was anonymous; customers use my/{id}/pdf (own agreements only)
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
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")] // creates a booking: staff incl. sales executives
        public async Task<IActionResult> MarkSigned(int id, [FromBody] MarkAgreementSignedRequest? req)
        {
            try
            {
                var existing = await _agreement.GetAgreementByIdAsync(id);
                if (existing == null)
                    return NotFound(new { isSuccessful = false, message = $"Agreement #{id} not found." });
                if (string.IsNullOrWhiteSpace(existing.SignedPdfPath))
                    return BadRequest(new { isSuccessful = false, message = "Upload the signed agreement first — a booking is only created once the signed copy is on file." });

                int? actorId = ResolveActorId();
                var result = await _agreement.MarkAgreementSignedAsync(id, actorId, req?.Note, existing.SignedDate ?? DateTime.Today);

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

        // ---------------- Customer portal (own agreements only) ----------------

        private string? CurrentUserEmail() =>
            User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;

        /// <summary>My Agreements: the logged-in customer's agreements.</summary>
        [HttpGet("my")]
        public async Task<IActionResult> GetMyAgreements()
        {
            var email = CurrentUserEmail();
            if (string.IsNullOrWhiteSpace(email)) return Unauthorized(new { isSuccessful = false, message = "User identity required." });
            var rows = await _agreement.GetMyAgreementsAsync(email);
            return Ok(new { isSuccessful = true, data = rows });
        }

        /// <summary>My Agreements: download the agreement sent to me.</summary>
        [HttpGet("my/{id:int}/pdf")]
        public async Task<IActionResult> DownloadMyAgreementPdf(int id)
        {
            var email = CurrentUserEmail();
            if (string.IsNullOrWhiteSpace(email) || !await _agreement.IsOwnAgreementAsync(email, id))
                return NotFound(new { isSuccessful = false, message = "Agreement not found." });
            try
            {
                var pdfBytes = await _agreement.GetAgreementPdfAsync(id);
                return File(pdfBytes, "application/pdf", $"WorkNest-Agreement-{id}.pdf");
            }
            catch (Exception ex)
            {
                return NotFound(new { isSuccessful = false, message = ex.Message });
            }
        }

        /// <summary>My Agreements: download the signed copy I uploaded.</summary>
        [HttpGet("my/{id:int}/signed-pdf")]
        public async Task<IActionResult> DownloadMySignedPdf(int id)
        {
            var email = CurrentUserEmail();
            if (string.IsNullOrWhiteSpace(email) || !await _agreement.IsOwnAgreementAsync(email, id))
                return NotFound(new { isSuccessful = false, message = "Agreement not found." });
            var agreement = await _agreement.GetAgreementByIdAsync(id);
            if (agreement == null || string.IsNullOrWhiteSpace(agreement.SignedPdfPath))
                return NotFound(new { isSuccessful = false, message = "No signed copy has been uploaded yet." });
            var path = Path.IsPathRooted(agreement.SignedPdfPath) ? agreement.SignedPdfPath : Path.Combine(_env.ContentRootPath, agreement.SignedPdfPath);
            if (!System.IO.File.Exists(path)) return NotFound(new { isSuccessful = false, message = "Signed copy is missing on the server." });
            return File(await System.IO.File.ReadAllBytesAsync(path), "application/pdf", $"WorkNest-Agreement-{id}-signed.pdf");
        }

        /// <summary>
        /// My Agreements: upload my signed copy + the date I signed. Held for admin verification
        /// (status SignedUploaded); the booking is created when an admin confirms it.
        /// </summary>
        [HttpPost("my/{id:int}/upload-signed")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> UploadMySignedAgreement(int id, IFormFile? file, [FromForm] DateTime? signedDate)
        {
            try
            {
                var email = CurrentUserEmail();
                if (string.IsNullOrWhiteSpace(email) || !await _agreement.IsOwnAgreementAsync(email, id))
                    return NotFound(new { isSuccessful = false, message = "Agreement not found." });
                if (signedDate == null)
                    return BadRequest(new { isSuccessful = false, message = "Enter the date you signed the agreement." });
                var date = signedDate.Value.Date;
                if (date > DateTime.Today)
                    return BadRequest(new { isSuccessful = false, message = "The signed date cannot be in the future." });

                var agreement = await _agreement.GetAgreementByIdAsync(id);
                if (agreement == null) return NotFound(new { isSuccessful = false, message = "Agreement not found." });
                if (agreement.BookingId != null)
                    return BadRequest(new { isSuccessful = false, message = "This agreement is already signed and your booking has been created." });
                if (agreement.SentDate != default && date < agreement.SentDate.Date)
                    return BadRequest(new { isSuccessful = false, message = $"The signed date cannot be before the agreement was sent ({agreement.SentDate:d MMM yyyy})." });

                var upload = await SaveSignedPdfAsync(id, file);
                if (upload.Error != null)
                    return BadRequest(new { isSuccessful = false, message = upload.Error });

                await _agreement.MarkCustomerSignedUploadAsync(id, date);
                return Ok(new { isSuccessful = true, message = "Thank you — your signed agreement was received. We will verify it and confirm your booking." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Signed agreement came back (one step): upload the signed scan + the date written on it.
        /// Marks the agreement signed, creates the booking from its quotation and dates the agreement,
        /// booking and challan on that date. The first invoice is then issued with the same date
        /// (POST api/booking/{bookingId}/send-initial-invoice?issuedOn=yyyy-MM-dd).
        /// </summary>
        [HttpPost("{id:int}/sign")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> SignAgreement(int id, IFormFile? file, [FromForm] DateTime? signedDate, [FromForm] string? note)
        {
            try
            {
                if (signedDate == null)
                    return BadRequest(new { isSuccessful = false, message = "Enter the date written on the signed agreement." });
                var date = signedDate.Value.Date;
                if (date > DateTime.Today)
                    return BadRequest(new { isSuccessful = false, message = "The signed date cannot be in the future." });

                var agreement = await _agreement.GetAgreementByIdAsync(id);
                if (agreement == null)
                    return NotFound(new { isSuccessful = false, message = $"Agreement #{id} not found." });
                if (agreement.BookingId != null)
                    return BadRequest(new { isSuccessful = false, message = $"This agreement is already signed (booking #{agreement.BookingId})." });
                if (agreement.SentDate != default && date < agreement.SentDate.Date)
                    return BadRequest(new { isSuccessful = false, message = $"The signed date cannot be before the agreement was sent ({agreement.SentDate:d MMM yyyy})." });

                // 1. Store the signed scan (same checks as upload-signed). When the customer already uploaded
                //    it from the portal, the admin is verifying that copy and a new file is optional.
                bool customerCopyOnFile = !string.IsNullOrWhiteSpace(agreement.SignedPdfPath);
                if (file != null || !customerCopyOnFile)
                {
                    var upload = await SaveSignedPdfAsync(id, file);
                    if (upload.Error != null)
                        return BadRequest(new { isSuccessful = false, message = upload.Error });
                }

                // 2. Mark signed + create the booking, dated on the agreement
                int? actorId = ResolveActorId();
                var result = await _agreement.MarkAgreementSignedAsync(id, actorId, note, date);

                return Ok(new
                {
                    isSuccessful = true,
                    data = result,
                    message = $"Signed agreement saved. Booking #{result.BookingId} created, dated {date:d MMM yyyy}."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        /// <summary>Validates and stores a signed agreement PDF; records its path on the agreement.</summary>
        private async Task<(string? Error, string? FileName, DateTime UploadedAtUtc)> SaveSignedPdfAsync(int id, IFormFile? file)
        {
            if (file == null || file.Length == 0) return ("Attach the signed agreement (PDF).", null, default);
            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext) || !ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                return ("Only PDF files (.pdf) are allowed.", null, default);
            if (file.Length > 10 * 1024 * 1024) return ("File size exceeds the 10MB limit.", null, default);
            await using (var stream = file.OpenReadStream())
            {
                var header = new byte[5];
                int read = await stream.ReadAsync(header, 0, 5);
                if (read < 5 || Encoding.ASCII.GetString(header) != "%PDF-")
                    return ("Invalid file signature. Uploaded file is not a valid PDF document.", null, default);
            }
            var configPath = string.IsNullOrWhiteSpace(_storageSettings.SignedAgreementsPath) ? "App_Data/SignedAgreements" : _storageSettings.SignedAgreementsPath;
            var targetDir = Path.IsPathRooted(configPath) ? configPath : Path.Combine(_env.ContentRootPath, configPath);
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
            var fileName = $"{id}_{Guid.NewGuid():N}.pdf";
            var fullFilePath = Path.Combine(targetDir, fileName);
            await using (var destStream = new FileStream(fullFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                await file.CopyToAsync(destStream);
            var uploadedAtUtc = DateTime.UtcNow;
            await _agreement.UpdateSignedPdfInfoAsync(id, fullFilePath, uploadedAtUtc);
            return (null, fileName, uploadedAtUtc);
        }

        [HttpPost("{id:int}/upload-signed")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]
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
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]
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
