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
                request.OperatingHours ?? "24/7",
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
                OperatingHours = row.TryGetValue("OperatingHours", out var oh) && oh != null ? oh.ToString() : "24/7",
                ContractStartDate = row.TryGetValue("StartDateTime", out var sdt) && sdt != null ? Convert.ToDateTime(sdt) : null,
                ContractEndDate = row.TryGetValue("EndDateTime", out var edt) && edt != null ? Convert.ToDateTime(edt) : null
            };

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

        public async Task<(IEnumerable<AgreementResponseDto> Rows, int Total)> GetAgreementsListAsync(int page, int limit, string? search, string? status)
        {
            var (rows, total) = await _db.GetAgreementsListDbAsync(page, limit, search, status);
            var list = new List<AgreementResponseDto>();

            foreach (var r in rows)
            {
                list.Add(new AgreementResponseDto
                {
                    Id = Convert.ToInt32(r["Id"]),
                    Guid = r["Guid"]?.ToString(),
                    QuotationId = Convert.ToInt32(r["QuotationId"]),
                    QuotationNumber = r.TryGetValue("QuotationNumber", out var qn) && qn != null ? qn.ToString() : "",
                    BookingId = r.TryGetValue("BookingId", out var bi) && bi != null && bi != DBNull.Value ? Convert.ToInt32(bi) : null,
                    CustomerId = Convert.ToInt32(r["CustomerId"]),
                    EntityType = r["EntityType"]?.ToString() ?? "Individual",
                    Status = r["Status"]?.ToString() ?? "AgreementSent",
                    SentDate = Convert.ToDateTime(r["SentDate"]),
                    SignedDate = r.TryGetValue("SignedDate", out var sdate) && sdate != null && sdate != DBNull.Value ? Convert.ToDateTime(sdate) : null,
                    RefundDays = r.TryGetValue("RefundDays", out var rd) && rd != null ? Convert.ToInt32(rd) : 30,
                    FeeAmount = r.TryGetValue("FeeAmount", out var fa) && fa != null ? Convert.ToDecimal(fa) : 0,
                    SecurityDeposit = r.TryGetValue("SecurityDeposit", out var sd) && sd != null ? Convert.ToDecimal(sd) : 0,
                    OperatingHours = r.TryGetValue("OperatingHours", out var oh) && oh != null ? oh.ToString() : null,
                    CustomerName = r["CustomerName"]?.ToString(),
                    CustomerCnic = r["CustomerCnic"]?.ToString(),
                    CustomerPhone = r["CustomerPhone"]?.ToString(),
                    CustomerAddress = r["CustomerAddress"]?.ToString(),
                    CompanyName = r["CompanyName"]?.ToString(),
                    Ntn = r["Ntn"]?.ToString(),
                    SecpRegistrationNo = r["SecpRegistrationNo"]?.ToString(),
                    TemplateVersionId = r.TryGetValue("TemplateVersionId", out var tId) && tId != null && tId != DBNull.Value ? Convert.ToInt32(tId) : null,
                    CreatedOn = Convert.ToDateTime(r["CreatedOn"])
                });
            }

            return (list, total);
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
    }
}
