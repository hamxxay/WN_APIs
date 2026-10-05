using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Agreement;
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

        public AgreementService(
            IDbRepository db,
            IQuotationService quotations,
            IEmailService email,
            IPdfService pdf,
            ILeaseTemplateService templateService,
            IHtmlToPdfService htmlToPdfService,
            IPdfMergeService pdfMergeService)
        {
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
            await _db.ExecuteRawSqlAsync($"UPDATE dbo.WN_Quotations SET Status = 'AgreementSent', UpdatedDate = SYSUTCDATETIME() WHERE Id = {q.Id}");

            // 7. Add Activity log
            string msg = $"Lease Agreement (Template: {activeTemplate.Name} v{activeTemplate.Id}) sent to {request.FullName ?? q.CustomerName} ({request.EntityType}).".Replace("'", "''");
            string uIdStr = userId.HasValue ? userId.Value.ToString() : "NULL";
            await _db.ExecuteRawSqlAsync($"INSERT INTO dbo.WN_QuotationActivities (IdGUID, QuotationId, Version, ActivityType, Message, CreatedByUserId, CreatedDate) VALUES (NEWID(), {q.Id}, {q.Version}, 'AgreementSent', '{msg}', {uIdStr}, SYSUTCDATETIME())");

            // 8. Send Email with attached PDF if email is present
            string recipientEmail = !string.IsNullOrWhiteSpace(request.OverrideEmail) ? request.OverrideEmail : (q.CustomerEmail ?? "");
            if (!string.IsNullOrWhiteSpace(recipientEmail))
            {
                await _email.SendAgreementEmailAsync(recipientEmail, request.FullName ?? q.CustomerName ?? "Valued Customer", qNum, mergedPdf);
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

        public async Task<AgreementResponseDto> MarkAgreementSignedAsync(int agreementId, int? userId, string? note)
        {
            var row = await _db.GetAgreementByIdDbAsync(agreementId);
            if (row == null) throw new InvalidOperationException("Agreement record not found.");

            int quotationId = Convert.ToInt32(row["QuotationId"]);

            // 1. Update Agreement to Signed
            await _db.MarkAgreementSignedDbAsync(agreementId, userId);

            // 2. Update Quotation Status to Signed
            await _db.ExecuteRawSqlAsync($"UPDATE dbo.WN_Quotations SET Status = 'Signed', UpdatedDate = SYSUTCDATETIME() WHERE Id = {quotationId}");

            // 3. Convert Quotation to Booking
            var convertRes = await _quotations.ConvertQuotationToBookingAsync(quotationId, userId);
            if (convertRes.TryGetValue("BookingId", out var bVal) && bVal != null)
            {
                int bookingId = Convert.ToInt32(bVal);
                await _db.ExecuteRawSqlAsync($"UPDATE dbo.WN_Agreements SET BookingId = {bookingId} WHERE Id = {agreementId}");
            }

            var (rows, _) = await GetAgreementsListAsync(1, 1, null, null);
            var updated = rows.FirstOrDefault(r => r.Id == agreementId);
            return updated ?? throw new InvalidOperationException("Failed to retrieve updated agreement.");
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
