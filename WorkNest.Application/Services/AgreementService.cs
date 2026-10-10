using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Agreement;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    public class AgreementService : IAgreementService
    {
        private readonly IDbRepository _db;
        private readonly IQuotationService _quotations;
        private readonly IEmailService _email;
        private readonly IPdfService _pdf;
        private readonly ILeaseTemplateService _templateService;
        private readonly IHtmlToPdfService _htmlToPdfService;
        private readonly IPdfMergeService _pdfMergeService;
        private readonly IBusinessClock _clock;
        private readonly ILogger<AgreementService> _logger;

        public AgreementService(
            IDbRepository db,
            IQuotationService quotations,
            IEmailService email,
            IPdfService pdf,
            ILeaseTemplateService templateService,
            IHtmlToPdfService htmlToPdfService,
            IPdfMergeService pdfMergeService,
            IBusinessClock clock,
            ILogger<AgreementService> logger)
        {
            _clock = clock;
            _logger = logger;
            _db = db;
            _quotations = quotations;
            _email = email;
            _pdf = pdf;
            _templateService = templateService;
            _htmlToPdfService = htmlToPdfService;
            _pdfMergeService = pdfMergeService;
        }

        private static string NormalizeOperatingHours(string? opHours)
        {
            if (string.IsNullOrWhiteSpace(opHours)) return "24/7";
            string raw = opHours.Trim();
            if (raw == "1" || raw.Equals("24-by-7", StringComparison.OrdinalIgnoreCase) || raw.Equals("24/7", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("24/7", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("24-by-7", StringComparison.OrdinalIgnoreCase))
                return "24/7";
            if (raw == "2" || raw.IndexOf("Morning", StringComparison.OrdinalIgnoreCase) >= 0)
                return "morning 6am to 6pm";
            if (raw == "3" || raw.IndexOf("Evening", StringComparison.OrdinalIgnoreCase) >= 0)
                return "evening 6pm to 6am";
            if (raw == "4" || raw.IndexOf("Shift", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Shift Access";
            return raw;
        }

        private static void ValidateAgreementData(SendAgreementRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (request.QuotationId <= 0)
                throw new InvalidOperationException("Valid QuotationId is required.");

            if (string.IsNullOrWhiteSpace(request.FullName) && string.IsNullOrWhiteSpace(request.CompanyName))
                throw new InvalidOperationException("Customer Name / Company Name is required for agreement generation.");

            if (string.IsNullOrWhiteSpace(request.Cnic) && string.IsNullOrWhiteSpace(request.SecpRegistrationNo) && string.IsNullOrWhiteSpace(request.Ntn))
                throw new InvalidOperationException("Customer CNIC, NTN, or SECP Registration Number is required.");

            var cnicError = Cnic.Validate(request.Cnic);
            if (cnicError != null)
                throw new InvalidOperationException(cnicError);

            var phoneError = Phone.Validate(request.PhoneNumber);
            if (phoneError != null)
                throw new InvalidOperationException(phoneError);

            if (string.IsNullOrWhiteSpace(request.Address))
                throw new InvalidOperationException("Customer Address is required.");

            if (request.FeeAmount <= 0)
                throw new InvalidOperationException("Valid recurring fee amount is required.");

            if (!request.ContractStartDate.HasValue || !request.ContractEndDate.HasValue)
                throw new InvalidOperationException("Contract start and end dates are required.");
        }

        public async Task<(byte[] PdfBytes, AgreementResponseDto Agreement)> GenerateLeaseAgreementAsync(GenerateLeaseAgreementRequest request, int? userId)
        {
            ValidateAgreementData(request);

            var q = await _quotations.GetQuotationByIdAsync(request.QuotationId)
                ?? throw new InvalidOperationException($"Quotation #{request.QuotationId} not found.");

            string templateName = !string.IsNullOrWhiteSpace(request.TemplateName) ? request.TemplateName : "StandardLeaseAgreement";
            var activeTemplate = await _templateService.GetActiveTemplateAsync(templateName)
                ?? throw new InvalidOperationException($"Active lease agreement template '{templateName}' not found.");

            // 1. Permanently update Customer details
            await _db.UpdateCustomerAgreementDetailsDbAsync(
                q.CustomerId,
                request.FullName,
                request.PhoneNumber,
                request.Cnic,
                request.Address,
                request.CompanyName,
                request.Ntn,
                request.SecpRegistrationNo
            );

            // Fetch dynamic location & company details from Quotation Space -> Location -> Branch -> Company
            var locComp = await _db.GetQuotationLocationAndCompanyDetailsDbAsync(q.Id);
            if (locComp != null)
            {
                if (string.IsNullOrWhiteSpace(request.CenterName) && locComp.TryGetValue("CenterName", out var cn) && cn != null)
                    request.CenterName = cn.ToString();
                if (string.IsNullOrWhiteSpace(request.VendorLegalName) && locComp.TryGetValue("VendorLegalName", out var vn) && vn != null)
                    request.VendorLegalName = vn.ToString();
                if (string.IsNullOrWhiteSpace(request.VendorAddress) && locComp.TryGetValue("VendorAddress", out var va) && va != null)
                    request.VendorAddress = va.ToString();
                if (string.IsNullOrWhiteSpace(request.VendorPhone) && locComp.TryGetValue("VendorPhone", out var vp) && vp != null)
                    request.VendorPhone = vp.ToString();
                if (string.IsNullOrWhiteSpace(request.VendorNtn) && locComp.TryGetValue("VendorNtn", out var vntn) && vntn != null)
                    request.VendorNtn = vntn.ToString();
            }

            string formattedOpHours = NormalizeOperatingHours(request.OperatingHours);
            request.OperatingHours = formattedOpHours;

            // 2. Generate Page 1 (Dynamic Schedule & Preamble) via QuestPDF
            string qNum = q.QuotationNumber ?? $"QTN-{q.Id}";
            byte[] page1Pdf = _pdf.GenerateAgreementPdf(request, qNum);

            // 3. Render Static Pages 2+ from stored active HTML via PuppeteerSharp
            byte[] staticPagesPdf = await _htmlToPdfService.ConvertHtmlToPdfAsync(activeTemplate.ContentHtml);

            // 4. Merge Page 1 and Static Pages into one combined PDF
            byte[] mergedPdf = _pdfMergeService.MergePdfs(new[] { page1Pdf, staticPagesPdf });

            // 5. Save Agreement record in WN_Agreements with pinned TemplateVersionId
            int agreementId = await _db.InsertAgreementDbAsync(
                q.Id,
                q.CustomerId,
                request.EntityType,
                request.RefundDays,
                request.FeeAmount > 0 ? request.FeeAmount : q.TotalAmount,
                request.SecurityDeposit > 0 ? request.SecurityDeposit : q.SecurityDeposit,
                formattedOpHours,
                request.FullName ?? q.CustomerName,
                request.Cnic,
                request.PhoneNumber,
                request.Address,
                request.CompanyName,
                request.Ntn,
                request.SecpRegistrationNo,
                userId,
                activeTemplate.Id
            );

            // 6. Update Quotation Status to AgreementSent
            await _db.UpdateQuotationStatusAsync(q.Id, "AgreementSent");

            // 7. Add Activity log
            string msg = $"Lease Agreement (Template: {activeTemplate.Name} v{activeTemplate.Id}) sent to {request.FullName ?? q.CustomerName} ({request.EntityType}).";
            await _db.AddQuotationActivityAsync(q.Id, q.Version, "AgreementSent", msg, userId);

            // 8. Send Email with attached PDF asynchronously without blocking HTTP response
            string recipientEmail = !string.IsNullOrWhiteSpace(request.OverrideEmail) ? request.OverrideEmail : (q.CustomerEmail ?? "");
            if (!string.IsNullOrWhiteSpace(recipientEmail))
            {
                string customerDisplay = request.FullName ?? q.CustomerName ?? "Valued Customer";
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _email.SendAgreementEmailAsync(recipientEmail, customerDisplay, qNum, mergedPdf);
                    }
                    catch (Exception ex)
                    {
                        // Make the failure visible: agreement shows "EmailFailed" in the admin list and the
                        // quotation's activity log records why. The customer can still get it from My Agreements.
                        try
                        {
                            await _db.SetAgreementStatusDbAsync(agreementId, "EmailFailed");
                            await _db.AddQuotationActivityAsync(q.Id, q.Version, "AgreementEmailFailed",
                                $"Agreement email to {recipientEmail} failed: {ex.Message}", userId);
                        }
                        catch { /* logging must never break the request */ }
                    }
                });
            }

            var dto = new AgreementResponseDto
            {
                Id = agreementId,
                QuotationId = q.Id,
                QuotationNumber = q.QuotationNumber,
                CustomerId = q.CustomerId,
                EntityType = request.EntityType,
                Status = "AgreementSent",
                SentDate = DateTime.UtcNow,
                RefundDays = request.RefundDays,
                FeeAmount = request.FeeAmount,
                SecurityDeposit = request.SecurityDeposit,
                OperatingHours = request.OperatingHours,
                CustomerName = request.FullName ?? q.CustomerName,
                CustomerCnic = request.Cnic,
                CustomerPhone = request.PhoneNumber,
                CustomerAddress = request.Address,
                CompanyName = request.CompanyName,
                Ntn = request.Ntn,
                SecpRegistrationNo = request.SecpRegistrationNo,
                TemplateVersionId = activeTemplate.Id,
                TemplateName = activeTemplate.Name,
                CreatedOn = DateTime.UtcNow
            };

            return (mergedPdf, dto);
        }

        public async Task<AgreementResponseDto> SendAgreementAsync(SendAgreementRequest request, int? userId)
        {
            var genReq = new GenerateLeaseAgreementRequest
            {
                QuotationId = request.QuotationId,
                EntityType = request.EntityType,
                FullName = request.FullName,
                Cnic = request.Cnic,
                PhoneNumber = request.PhoneNumber,
                Address = request.Address,
                CompanyName = request.CompanyName,
                Ntn = request.Ntn,
                SecpRegistrationNo = request.SecpRegistrationNo,
                RefundDays = request.RefundDays,
                FeeAmount = request.FeeAmount,
                SecurityDeposit = request.SecurityDeposit,
                ContractStartDate = request.ContractStartDate,
                ContractEndDate = request.ContractEndDate,
                OperatingHours = request.OperatingHours,
                BillingFrequency = request.BillingFrequency,
                OverrideEmail = request.OverrideEmail,
                TemplateName = "StandardLeaseAgreement"
            };

            var (_, dto) = await GenerateLeaseAgreementAsync(genReq, userId);
            return dto;
        }

        public async Task<byte[]> GetAgreementPdfAsync(int agreementId)
        {
            var row = await _db.GetAgreementByIdDbAsync(agreementId);
            if (row == null) throw new InvalidOperationException($"Agreement #{agreementId} not found.");

            var req = new SendAgreementRequest
            {
                QuotationId = Convert.ToInt32(row["QuotationId"]),
                EntityType = row["EntityType"]?.ToString() ?? "Individual",
                FullName = row["CustomerName"]?.ToString(),
                Cnic = row["CustomerCnic"]?.ToString(),
                PhoneNumber = row["CustomerPhone"]?.ToString(),
                Address = row["CustomerAddress"]?.ToString(),
                CompanyName = row["CompanyName"]?.ToString(),
                Ntn = row["Ntn"]?.ToString(),
                SecpRegistrationNo = row["SecpRegistrationNo"]?.ToString(),
                RefundDays = row.TryGetValue("RefundDays", out var rd) && rd != null ? Convert.ToInt32(rd) : 30,
                FeeAmount = row.TryGetValue("FeeAmount", out var fa) && fa != null ? Convert.ToDecimal(fa) : 0,
                SecurityDeposit = row.TryGetValue("SecurityDeposit", out var sd) && sd != null ? Convert.ToDecimal(sd) : 0,
                OperatingHours = NormalizeOperatingHours(row.TryGetValue("OperatingHours", out var oh) && oh != null ? oh.ToString() : "24/7"),
                ContractStartDate = row.TryGetValue("StartDateTime", out var sdt) && sdt != null ? Convert.ToDateTime(sdt) : null,
                ContractEndDate = row.TryGetValue("EndDateTime", out var edt) && edt != null ? Convert.ToDateTime(edt) : null,
                CenterName = row.TryGetValue("CenterName", out var cn) && cn != null ? cn.ToString() : null,
                VendorLegalName = row.TryGetValue("VendorLegalName", out var vn) && vn != null ? vn.ToString() : null,
                VendorAddress = row.TryGetValue("VendorAddress", out var va) && va != null ? va.ToString() : null,
                VendorPhone = row.TryGetValue("VendorPhone", out var vp) && vp != null ? vp.ToString() : null,
                VendorNtn = row.TryGetValue("VendorNtn", out var vntn) && vntn != null ? vntn.ToString() : null
            };

            if (string.IsNullOrWhiteSpace(req.VendorLegalName) || string.IsNullOrWhiteSpace(req.VendorAddress))
            {
                var locComp = await _db.GetQuotationLocationAndCompanyDetailsDbAsync(req.QuotationId);
                if (locComp != null)
                {
                    if (string.IsNullOrWhiteSpace(req.CenterName) && locComp.TryGetValue("CenterName", out var locCn) && locCn != null)
                        req.CenterName = locCn.ToString();
                    if (string.IsNullOrWhiteSpace(req.VendorLegalName) && locComp.TryGetValue("VendorLegalName", out var locVn) && locVn != null)
                        req.VendorLegalName = locVn.ToString();
                    if (string.IsNullOrWhiteSpace(req.VendorAddress) && locComp.TryGetValue("VendorAddress", out var locVa) && locVa != null)
                        req.VendorAddress = locVa.ToString();
                    if (string.IsNullOrWhiteSpace(req.VendorPhone) && locComp.TryGetValue("VendorPhone", out var locVp) && locVp != null)
                        req.VendorPhone = locVp.ToString();
                    if (string.IsNullOrWhiteSpace(req.VendorNtn) && locComp.TryGetValue("VendorNtn", out var locVntn) && locVntn != null)
                        req.VendorNtn = locVntn.ToString();
                }
            }

            string qNum = row["QuotationNumber"]?.ToString() ?? $"QTN-{req.QuotationId}";

            // 1. Generate Page 1 (Dynamic)
            byte[] page1Pdf = _pdf.GenerateAgreementPdf(req, qNum);

            // 2. Load stored template HTML
            string? templateHtml = row.TryGetValue("TemplateHtml", out var th) && th != null ? th.ToString() : null;
            if (string.IsNullOrWhiteSpace(templateHtml))
            {
                var activeTemplate = await _templateService.GetActiveTemplateAsync("StandardLeaseAgreement");
                templateHtml = activeTemplate?.ContentHtml ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(templateHtml))
            {
                byte[] staticPagesPdf = await _htmlToPdfService.ConvertHtmlToPdfAsync(templateHtml);
                return _pdfMergeService.MergePdfs(new[] { page1Pdf, staticPagesPdf });
            }

            return page1Pdf;
        }

        private static string? GetString(IDictionary<string, object?> r, string key)
        {
            return r.TryGetValue(key, out var val) && val != null && val != DBNull.Value ? val.ToString() : null;
        }

        private static int GetInt(IDictionary<string, object?> r, string key, int defaultVal = 0)
        {
            if (r.TryGetValue(key, out var val) && val != null && val != DBNull.Value && int.TryParse(val.ToString(), out var i))
                return i;
            return defaultVal;
        }

        private static int? GetNullableInt(IDictionary<string, object?> r, string key)
        {
            if (r.TryGetValue(key, out var val) && val != null && val != DBNull.Value && int.TryParse(val.ToString(), out var i))
                return i;
            return null;
        }

        private static decimal GetDecimal(IDictionary<string, object?> r, string key, decimal defaultVal = 0)
        {
            if (r.TryGetValue(key, out var val) && val != null && val != DBNull.Value && decimal.TryParse(val.ToString(), out var d))
                return d;
            return defaultVal;
        }

        private static DateTime GetDateTime(IDictionary<string, object?> r, string key, DateTime defaultVal = default)
        {
            if (r.TryGetValue(key, out var val) && val != null && val != DBNull.Value && DateTime.TryParse(val.ToString(), out var dt))
                return dt;
            return defaultVal;
        }

        private static DateTime? GetNullableDateTime(IDictionary<string, object?> r, string key)
        {
            if (r.TryGetValue(key, out var val) && val != null && val != DBNull.Value && DateTime.TryParse(val.ToString(), out var dt))
                return dt;
            return null;
        }

        private static AgreementResponseDto MapToAgreementDto(IDictionary<string, object?> r)
        {
            return new AgreementResponseDto
            {
                Id = GetInt(r, "Id"),
                Guid = GetString(r, "Guid"),
                QuotationId = GetInt(r, "QuotationId"),
                QuotationNumber = GetString(r, "QuotationNumber") ?? "",
                BookingId = GetNullableInt(r, "BookingId"),
                CustomerId = GetInt(r, "CustomerId"),
                EntityType = GetString(r, "EntityType") ?? "Individual",
                Status = GetString(r, "Status") ?? "AgreementSent",
                SentDate = GetDateTime(r, "SentDate", DateTime.UtcNow),
                SignedDate = GetNullableDateTime(r, "SignedDate"),
                RefundDays = GetInt(r, "RefundDays", 30),
                FeeAmount = GetDecimal(r, "FeeAmount"),
                SecurityDeposit = GetDecimal(r, "SecurityDeposit"),
                OperatingHours = GetString(r, "OperatingHours"),
                CustomerName = GetString(r, "CustomerName"),
                CustomerCnic = GetString(r, "CustomerCnic"),
                CustomerPhone = GetString(r, "CustomerPhone"),
                CustomerAddress = GetString(r, "CustomerAddress"),
                CompanyName = GetString(r, "CompanyName"),
                Ntn = GetString(r, "Ntn"),
                SecpRegistrationNo = GetString(r, "SecpRegistrationNo"),
                TemplateVersionId = GetNullableInt(r, "TemplateVersionId"),
                TemplateName = GetString(r, "TemplateName"),
                SignedPdfPath = GetString(r, "SignedPdfPath"),
                SignedPdfUploadedAt = GetNullableDateTime(r, "SignedPdfUploadedAt"),
                CreatedOn = GetDateTime(r, "CreatedOn", DateTime.UtcNow)
            };
        }

        public async Task<(IEnumerable<AgreementResponseDto> Rows, int Total)> GetAgreementsListAsync(int page, int limit, string? search, string? status)
        {
            var (rows, total) = await _db.GetAgreementsListDbAsync(page, limit, search, status);
            var list = new List<AgreementResponseDto>();

            foreach (var r in rows)
            {
                list.Add(MapToAgreementDto(r));
            }

            // Signature reminders sent (one extra query for the page); the list still loads if it fails.
            if (list.Count > 0)
            {
                try
                {
                    var stats = await _db.GetAgreementReminderStatsDbAsync(list.Select(a => a.Id));
                    foreach (var a in list)
                    {
                        if (stats.TryGetValue(a.Id, out var st))
                        {
                            a.ReminderCount = st.Count;
                            a.LastReminderAt = st.LastReminderAt;
                        }
                    }
                }
                catch { /* reminder info is optional */ }

                // How it was signed: e-signed in the portal (WN_AgreementESignatures) vs an uploaded scan. Optional too.
                try
                {
                    var esigned = await _db.GetAgreementESignatureStatsDbAsync(list.Select(a => a.Id));
                    foreach (var a in list)
                    {
                        if (esigned.TryGetValue(a.Id, out var es))
                        {
                            a.ESignedBy = es.SignerName;
                            a.ESignedAt = es.SignedAt;
                        }
                    }
                }
                catch { /* e-signature info is optional */ }
            }

            return (list, total);
        }

        public async Task<AgreementResponseDto?> GetAgreementByIdAsync(int agreementId)
        {
            var r = await _db.GetAgreementByIdDbAsync(agreementId);
            if (r == null) return null;

            return MapToAgreementDto(r);
        }

        public async Task UpdateSignedPdfInfoAsync(int agreementId, string path, DateTime uploadedAtUtc)
        {
            await _db.UpdateAgreementSignedPdfDbAsync(agreementId, path, uploadedAtUtc);
        }

        /// <summary>
        /// Signed agreement came back: mark it signed, create the booking from its quotation, and date the
        /// agreement, booking and challan on <paramref name="signedDate"/> (the date on the signed agreement).
        /// </summary>
        private async Task<int> ResolveCustomerIdAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return 0;
            var user = await _db.GetUserByEmailAsync(email);
            if (user == null) return 0;
            int customerId = user.TryGetValue("CustomerId", out var cid) && cid != null ? Convert.ToInt32(cid) : 0;
            int userId = user.TryGetValue("Id", out var uid) && uid != null ? Convert.ToInt32(uid) : 0;
            if (customerId <= 0 && userId > 0)
            {
                var cust = await _db.GetCustomerByUserIdAsync(userId);
                if (cust != null && cust.TryGetValue("Id", out var custId) && custId != null) customerId = Convert.ToInt32(custId);
            }
            return customerId;
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetMyAgreementsAsync(string email)
        {
            int customerId = await ResolveCustomerIdAsync(email);
            return customerId > 0 ? await _db.GetCustomerAgreementsDbAsync(customerId) : Enumerable.Empty<IDictionary<string, object?>>();
        }

        public async Task<bool> IsOwnAgreementAsync(string email, int agreementId)
        {
            int customerId = await ResolveCustomerIdAsync(email);
            if (customerId <= 0) return false;
            return (await _db.GetCustomerAgreementsDbAsync(customerId)).Any(r => r.TryGetValue("Id", out var id) && id != null && Convert.ToInt32(id) == agreementId);
        }

        public async Task MarkCustomerSignedUploadAsync(int agreementId, DateTime signedDate)
        {
            await _db.SetAgreementStatusDbAsync(agreementId, "SignedUploaded", signedDate);
        }

        // One conversion at a time per quotation: a double click, or a customer upload racing an admin "mark signed",
        // used to create two bookings (each with its own deposit and invoices).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, System.Threading.SemaphoreSlim> ConvertLocks = new();

        public async Task<AgreementResponseDto> MarkAgreementSignedAsync(int agreementId, int? userId, string? note, DateTime? signedDate = null)
        {
            var row = await _db.GetAgreementByIdDbAsync(agreementId);
            if (row == null) throw new InvalidOperationException("Agreement record not found.");
            int quotationId = Convert.ToInt32(row["QuotationId"]);

            var gate = ConvertLocks.GetOrAdd(quotationId, _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                return await MarkAgreementSignedLockedAsync(agreementId, quotationId, userId, signedDate);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<AgreementResponseDto> MarkAgreementSignedLockedAsync(int agreementId, int quotationId, int? userId, DateTime? signedDate)
        {
            // Re-read under the lock: a request that waited must not convert a second time.
            var current = await GetAgreementByIdAsync(agreementId)
                          ?? throw new InvalidOperationException("Agreement record not found.");
            if (current.BookingId is > 0) return current; // already converted (e.g. the first of two clicks)
            var quotation = await _db.GetQuotationByIdAsync(quotationId);
            if (quotation != null && quotation.TryGetValue("Status", out var qs) && string.Equals(qs?.ToString(), "Converted", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This quotation has already been converted to a booking. Open the booking from Bookings instead of signing it again.");

            // 1. Update Agreement to Signed
            await _db.MarkAgreementSignedDbAsync(agreementId, userId);

            // 2. Update Quotation Status to Signed
            await _db.UpdateQuotationStatusAsync(quotationId, "Signed");

            // 3. Convert Quotation to Booking
            var convertRes = await _quotations.ConvertQuotationToBookingAsync(quotationId, userId);
            int? bookingId = null;
            if (convertRes.TryGetValue("BookingId", out var bVal) && bVal != null)
            {
                bookingId = Convert.ToInt32(bVal);
                await _db.SetAgreementBookingIdAsync(agreementId, bookingId.Value);
            }
            if (bookingId == null)
                throw new InvalidOperationException("The agreement was marked signed but no booking was created. Check the quotation's space and dates.");

            // 4. Date everything on the agreement's signed date
            await _db.ApplyAgreementSignedDateDbAsync(agreementId, bookingId, (signedDate ?? _clock.Today).Date);

            // Re-read this agreement (the old code read only the newest one and failed for older agreements)
            return await GetAgreementByIdAsync(agreementId)
                   ?? throw new InvalidOperationException("Failed to retrieve updated agreement.");
        }

        // ---------------- Electronic signature (customer portal) ----------------

        /// <summary>
        /// The e-signed agreement: the agreement exactly as the customer downloads it (my/{id}/pdf) followed by the
        /// "Electronic signature certificate" page. Sets <see cref="AgreementESignatureEvidence.DocumentSha256"/> to the
        /// SHA-256 of the agreement as presented for signing (before the certificate is added).
        /// </summary>
        public async Task<byte[]> BuildESignedPdfAsync(AgreementESignatureEvidence evidence)
        {
            byte[] original = await GetAgreementPdfAsync(evidence.AgreementId);
            evidence.DocumentSha256 = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
            byte[] certificate = _pdf.GenerateESignatureCertificatePdf(evidence);
            return _pdfMergeService.MergePdfs(new[] { original, certificate });
        }

        /// <summary>
        /// The e-signed PDF is stored: hold the agreement as "SignedUploaded" (signed today), record the evidence and the
        /// activity, then create the booking exactly like an admin "mark signed". When the booking can't be created
        /// (e.g. the quotation's space or dates need attention) the agreement stays "SignedUploaded" with the signed copy
        /// on file, so staff finish it with the existing "mark signed".
        /// </summary>
        public async Task<ESignAgreementResult> CompleteESignatureAsync(AgreementESignatureEvidence evidence, int? userId)
        {
            int agreementId = evidence.AgreementId;
            DateTime signedDate = evidence.SignedAt.Date;

            // 1. Same state as a portal upload until the booking exists
            await _db.SetAgreementStatusDbAsync(agreementId, "SignedUploaded", signedDate);

            // 2. Evidence row; the certificate page in the signed PDF is the primary evidence, so this never blocks signing
            try
            {
                if (!await _db.InsertAgreementESignatureDbAsync(evidence))
                    _logger.LogWarning("WN_AgreementESignatures is missing (run Database/Agreements/WN_AgreementESignatures.txt); e-signature of agreement {AgreementId} is recorded only in its signed PDF.", agreementId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not record the e-signature evidence of agreement {AgreementId}.", agreementId);
            }

            // 3. Quotation activity (like AgreementSent / AgreementEmailFailed)
            var agreement = await GetAgreementByIdAsync(agreementId)
                            ?? throw new InvalidOperationException("Agreement record not found.");
            int quotationVersion = 0;
            try
            {
                var q = await _quotations.GetQuotationByIdAsync(agreement.QuotationId);
                quotationVersion = q?.Version ?? 0;
                await _db.AddQuotationActivityAsync(agreement.QuotationId, quotationVersion, "AgreementESigned",
                    $"Lease Agreement #{agreementId} signed electronically by {evidence.SignerName} ({evidence.SignerEmail}) on {evidence.SignedAt:dd MMM yyyy HH:mm} PKT from {evidence.IpAddress ?? "unknown IP"}.", userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not log the AgreementESigned activity of agreement {AgreementId}.", agreementId);
            }

            // 4. Create the booking like admin "mark signed" (serialised per quotation and idempotent)
            try
            {
                var signed = await MarkAgreementSignedAsync(agreementId, userId, "Signed electronically", signedDate);
                return new ESignAgreementResult { AgreementId = agreementId, Completed = signed.BookingId is > 0, BookingId = signed.BookingId, Status = signed.Status };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "E-signed agreement {AgreementId}: the booking was not created; left for staff to confirm.", agreementId);

                var after = await GetAgreementByIdAsync(agreementId);
                if (after?.BookingId is > 0) // converted after all (failure after the booking was created)
                    return new ESignAgreementResult { AgreementId = agreementId, Completed = true, BookingId = after.BookingId, Status = after.Status };

                // MarkAgreementSigned may have set the agreement to Signed before the conversion failed: put it back
                // (no-op once a booking exists) so it shows as "Signed copy received — verify" for staff.
                try
                {
                    await _db.SetAgreementStatusDbAsync(agreementId, "SignedUploaded", signedDate);
                    await _db.AddQuotationActivityAsync(agreement.QuotationId, quotationVersion, "AgreementESignPending",
                        $"Lease Agreement #{agreementId} was signed electronically but the booking could not be created automatically: {ex.Message} Confirm it with \"Mark signed\".", userId);
                }
                catch (Exception logEx)
                {
                    _logger.LogWarning(logEx, "Could not flag e-signed agreement {AgreementId} for staff.", agreementId);
                }
                return new ESignAgreementResult { AgreementId = agreementId, Completed = false, Status = "SignedUploaded" };
            }
        }

        /// <summary>Staff: the latest e-signature of an agreement (null when it was not e-signed).</summary>
        public async Task<AgreementESignatureDto?> GetAgreementESignatureAsync(int agreementId)
        {
            var r = await _db.GetAgreementESignatureDbAsync(agreementId);
            if (r == null) return null;
            return new AgreementESignatureDto
            {
                AgreementId = GetInt(r, "AgreementId"),
                SignerName = GetString(r, "SignerName"),
                SignerEmail = GetString(r, "SignerEmail"),
                SignedAt = GetDateTime(r, "SignedAt"),
                IpAddress = GetString(r, "IpAddress"),
                UserAgent = GetString(r, "UserAgent"),
                DocumentSha256 = GetString(r, "DocumentSha256")?.Trim(),
                SignatureImage = r.TryGetValue("SignatureImage", out var img) && img is byte[] bytes && bytes.Length > 0
                    ? "data:image/png;base64," + Convert.ToBase64String(bytes)
                    : null
            };
        }

        public async Task<bool> DeleteAgreementAsync(int agreementId)
        {
            var agreement = await GetAgreementByIdAsync(agreementId);
            if (agreement == null) return false;

            if (!string.IsNullOrWhiteSpace(agreement.SignedPdfPath) && File.Exists(agreement.SignedPdfPath))
            {
                try { File.Delete(agreement.SignedPdfPath); } catch { }
            }

            return await _db.DeleteAgreementDbAsync(agreementId);
        }

        public async Task<bool> DeleteSignedPdfAsync(int agreementId)
        {
            var agreement = await GetAgreementByIdAsync(agreementId);
            if (agreement == null) return false;

            if (!string.IsNullOrWhiteSpace(agreement.SignedPdfPath) && File.Exists(agreement.SignedPdfPath))
            {
                try { File.Delete(agreement.SignedPdfPath); } catch { }
            }

            await _db.ClearAgreementSignedPdfDbAsync(agreementId);
            return true;
        }
    }
}
