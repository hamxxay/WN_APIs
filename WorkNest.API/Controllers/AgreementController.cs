using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkNest.Application.DTOs.Agreement;
using WorkNest.Application.Interfaces;
using WorkNest.API.Filters;
using WorkNest.Common.Configurations;
using Microsoft.AspNetCore.RateLimiting;

namespace WorkNest.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Route("api/agreements")]
    [Authorize]
    [RecordScope(RecordKind.Agreement, "id")] // location-bound staff: only records of their locations
    public class AgreementController : ControllerBase
    {
        private readonly IAgreementService _agreement;
        private readonly IWebHostEnvironment _env;
        private readonly FileStorageSettings _storageSettings;
        private readonly IBusinessClock _clock;
        private readonly ILogger<AgreementController> _logger;

        public AgreementController(
            IAgreementService agreement,
            IWebHostEnvironment env,
            IOptions<FileStorageSettings> storageSettings,
            IBusinessClock clock,
            ILogger<AgreementController> logger)
        {
            _clock = clock;
            _logger = logger;
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

        [EnableRateLimiting("pdf")]
        [HttpPost("send")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")] // staff only
        public async Task<IActionResult> SendAgreement([FromBody] SendAgreementRequest request)
        {
            try
            {
                if (request == null || request.QuotationId <= 0)
                    return BadRequest(new { isSuccessful = false, message = "Valid QuotationId is required." });

                var cnicError = WorkNest.Application.Helpers.Cnic.Validate(request.Cnic);
                if (cnicError != null)
                    return BadRequest(new { isSuccessful = false, message = cnicError });

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

        [EnableRateLimiting("pdf")]
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
                var result = await _agreement.MarkAgreementSignedAsync(id, actorId, req?.Note, existing.SignedDate ?? _clock.Today);

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
        [EnableRateLimiting("pdf")]
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
                if (date > _clock.Today)
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

        // One e-signature at a time per agreement (double click / two tabs): the second request is turned away.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, System.Threading.SemaphoreSlim> ESignLocks = new();
        private const int MaxSignatureImageBytes = 300 * 1024;
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>
        /// My Agreements: sign electronically. Stores the agreement + an "Electronic signature certificate" page
        /// (signer, Pakistan time, email, IP, browser, signature image, SHA-256 of the agreement) as the signed copy,
        /// then creates the booking like admin "mark signed". If the booking can't be created automatically the
        /// signed copy is kept as "SignedUploaded" and staff confirm it.
        /// </summary>
        [EnableRateLimiting("pdf")]
        [HttpPost("my/{id:int}/esign")]
        [RequestSizeLimit(1024 * 1024)]
        public async Task<IActionResult> ESignMyAgreement(int id, [FromBody] ESignAgreementRequest? req)
        {
            var email = CurrentUserEmail();
            if (string.IsNullOrWhiteSpace(email) || !await _agreement.IsOwnAgreementAsync(email, id))
                return NotFound(new { isSuccessful = false, message = "Agreement not found." });

            // 1. Request
            if (req == null) return BadRequest(new { isSuccessful = false, message = "Please sign the agreement first." });
            if (!req.Consent)
                return BadRequest(new { isSuccessful = false, message = "Please confirm that you have read the agreement and agree to sign it electronically." });
            var signerName = (req.SignerName ?? "").Trim();
            if (signerName.Length < 2 || signerName.Length > 100)
                return BadRequest(new { isSuccessful = false, message = "Enter your full name (2 to 100 characters)." });
            var signature = DecodePngDataUrl(req.SignatureImage, out var signatureError);
            if (signature == null) return BadRequest(new { isSuccessful = false, message = signatureError });

            var gate = ESignLocks.GetOrAdd(id, _ => new System.Threading.SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0))
                return Conflict(new { isSuccessful = false, message = "Your signature is already being processed. Please wait a moment." });
            try
            {
                // 2. Agreement can be signed (read under the lock)
                var agreement = await _agreement.GetAgreementByIdAsync(id);
                if (agreement == null) return NotFound(new { isSuccessful = false, message = "Agreement not found." });
                if (agreement.BookingId != null)
                    return BadRequest(new { isSuccessful = false, message = "This agreement is already signed and your booking has been created." });
                var status = (agreement.Status ?? "").Trim();
                if (status.Equals("SignedUploaded", StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { isSuccessful = false, message = "Your signed agreement is already with our team. We will confirm your booking shortly." });
                if (!(status.Equals("AgreementSent", StringComparison.OrdinalIgnoreCase) || status.Equals("Sent", StringComparison.OrdinalIgnoreCase) || status.Equals("EmailFailed", StringComparison.OrdinalIgnoreCase)))
                    return BadRequest(new { isSuccessful = false, message = "This agreement can no longer be signed online. Please contact us." });
                if (await IsSupersededAsync(email, agreement))
                    return BadRequest(new { isSuccessful = false, message = "A newer version of this agreement was sent to you. Please sign the latest one." });

                // 3. Signed PDF = agreement as downloaded + certificate page
                var evidence = new AgreementESignatureEvidence
                {
                    AgreementId = id,
                    QuotationNumber = agreement.QuotationNumber,
                    CustomerName = !string.IsNullOrWhiteSpace(agreement.CompanyName) ? $"{agreement.CompanyName} ({agreement.CustomerName})" : agreement.CustomerName,
                    SignerName = signerName,
                    SignerEmail = Truncate(email, 256),
                    SignedAt = _clock.Now,
                    IpAddress = Truncate(HttpContext.Connection.RemoteIpAddress?.ToString(), 64),
                    UserAgent = Truncate(Request.Headers.UserAgent.ToString(), 400),
                    SignatureImage = signature
                };
                byte[] signedPdf;
                try
                {
                    signedPdf = await _agreement.BuildESignedPdfAsync(evidence);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "E-sign: could not build the signed PDF of agreement {AgreementId}.", id);
                    return StatusCode(500, new { isSuccessful = false, message = "We could not prepare your signed agreement. Please try again in a few minutes." });
                }

                // 4. Store it as the signed copy (same folder + DB update as upload-signed)
                await StoreSignedPdfBytesAsync(id, signedPdf);

                // 5. Evidence, activity and booking
                var result = await _agreement.CompleteESignatureAsync(evidence, ResolveActorId());
                return Ok(new
                {
                    isSuccessful = true,
                    data = result,
                    message = result.Completed
                        ? "Thank you — your agreement is signed and your booking has been created."
                        : "Signed. Our team will confirm your booking shortly."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "E-sign of agreement {AgreementId} failed.", id);
                return StatusCode(500, new { isSuccessful = false, message = "We could not complete your signature. Please try again." });
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>Staff: how an agreement was e-signed (signer, time, email, IP, browser, document hash, signature).</summary>
        [HttpGet("{id:int}/esignature")]
        [Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]
        public async Task<IActionResult> GetESignature(int id)
        {
            try
            {
                var row = await _agreement.GetAgreementESignatureAsync(id);
                return Ok(new { isSuccessful = true, data = row });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccessful = false, message = ex.Message });
            }
        }

        /// <summary>True when a newer agreement was sent to this customer for the same quotation.</summary>
        private async Task<bool> IsSupersededAsync(string email, AgreementResponseDto agreement)
        {
            var mine = await _agreement.GetMyAgreementsAsync(email);
            return mine.Any(r => r.TryGetValue("QuotationId", out var q) && q != null && Convert.ToInt32(q) == agreement.QuotationId
                              && r.TryGetValue("Id", out var aid) && aid != null && Convert.ToInt32(aid) > agreement.Id);
        }

        /// <summary>"data:image/png;base64,..." → PNG bytes (max 300 KB), or null with a message for the customer.</summary>
        private static byte[]? DecodePngDataUrl(string? dataUrl, out string error)
        {
            error = "Please draw or type your signature.";
            const string prefix = "data:image/png;base64,";
            if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            var b64 = dataUrl.Substring(prefix.Length).Trim();
            if (b64.Length == 0) return null;
            if (b64.Length > (MaxSignatureImageBytes / 3 + 1) * 4)
            {
                error = "The signature image is too large. Please clear it and sign again.";
                return null;
            }
            var buffer = new byte[b64.Length * 3 / 4 + 3];
            if (!Convert.TryFromBase64String(b64, buffer, out int written))
            {
                error = "The signature could not be read. Please clear it and sign again.";
                return null;
            }
            // PNG signature + IHDR with a sensible size (a signature, not a page-sized image)
            if (written < 33 || written > MaxSignatureImageBytes || !buffer.AsSpan(0, 8).SequenceEqual(PngSignature))
            {
                error = "The signature could not be read. Please clear it and sign again.";
                return null;
            }
            int width = (buffer[16] << 24) | (buffer[17] << 16) | (buffer[18] << 8) | buffer[19];
            int height = (buffer[20] << 24) | (buffer[21] << 16) | (buffer[22] << 8) | buffer[23];
            if (width < 10 || height < 10 || width > 4000 || height > 2000)
            {
                error = "The signature could not be read. Please clear it and sign again.";
                return null;
            }
            return buffer.AsSpan(0, written).ToArray();
        }

        private static string? Truncate(string? value, int max) =>
            string.IsNullOrEmpty(value) ? null : (value.Length <= max ? value : value.Substring(0, max));

        /// <summary>Signed agreements folder (FileStorage:SignedAgreementsPath, default App_Data/SignedAgreements).</summary>
        private string SignedAgreementsDirectory()
        {
            var configPath = string.IsNullOrWhiteSpace(_storageSettings.SignedAgreementsPath) ? "App_Data/SignedAgreements" : _storageSettings.SignedAgreementsPath;
            var targetDir = Path.IsPathRooted(configPath) ? configPath : Path.Combine(_env.ContentRootPath, configPath);
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
            return targetDir;
        }

        /// <summary>Stores a generated signed agreement PDF the same way as an uploaded one; records its path on the agreement.</summary>
        private async Task StoreSignedPdfBytesAsync(int id, byte[] pdf)
        {
            var fullFilePath = Path.Combine(SignedAgreementsDirectory(), $"{id}_{Guid.NewGuid():N}.pdf");
            await System.IO.File.WriteAllBytesAsync(fullFilePath, pdf);
            await _agreement.UpdateSignedPdfInfoAsync(id, fullFilePath, DateTime.UtcNow);
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
                if (date > _clock.Today)
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
