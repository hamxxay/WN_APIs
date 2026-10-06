using System;
using System.Threading.Tasks;
using Ganss.Xss;
using WorkNest.Application.DTOs.Agreement;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    public class LeaseTemplateService : ILeaseTemplateService
    {
        private readonly IDbRepository _db;
        private readonly HtmlSanitizer _sanitizer;

        public LeaseTemplateService(IDbRepository db)
        {
            _db = db;
            _sanitizer = new HtmlSanitizer();
            // Allow common formatting and structural elements & attributes
            _sanitizer.AllowedAttributes.Add("class");
            _sanitizer.AllowedAttributes.Add("style");
            _sanitizer.AllowedAttributes.Add("id");
            _sanitizer.AllowedCssProperties.Add("text-align");
            _sanitizer.AllowedCssProperties.Add("page-break-before");
            _sanitizer.AllowedCssProperties.Add("page-break-after");
            _sanitizer.AllowedCssProperties.Add("margin-top");
            _sanitizer.AllowedCssProperties.Add("margin-bottom");

            // Templates are rendered to PDF by a headless browser on the server, so no URL in them may
            // point at the network or file system. Only data: URLs (embedded base64 images) survive, in
            // src/href attributes and CSS url(...) alike; in-page "#anchor" links are kept.
            _sanitizer.AllowedSchemes.Clear();
            _sanitizer.AllowedSchemes.Add("data");
            _sanitizer.FilterUrl += (_, e) =>
            {
                var url = e.SanitizedUrl?.Trim();
                if (string.IsNullOrEmpty(url)) return;
                if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("#")) return;
                e.SanitizedUrl = null; // relative or other-scheme URL
            };
        }

        public async Task<LeaseTemplateResponseDto?> GetActiveTemplateAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "StandardLeaseAgreement";

            var row = await _db.GetActiveLeaseTemplateByNameDbAsync(name);
            if (row == null)
            {
                return null;
            }

            return new LeaseTemplateResponseDto
            {
                Id = Convert.ToInt32(row["Id"]),
                Name = row["Name"]?.ToString() ?? name,
                ContentHtml = row["ContentHtml"]?.ToString() ?? string.Empty,
                IsActive = Convert.ToBoolean(row["IsActive"]),
                CreatedAt = Convert.ToDateTime(row["CreatedAt"]),
                CreatedBy = row.TryGetValue("CreatedBy", out var cb) && cb != null && cb != DBNull.Value ? Convert.ToInt32(cb) : null,
                CreatedByName = row.TryGetValue("CreatedByName", out var cbn) && cbn != null ? cbn.ToString() : null
            };
        }

        public async Task<LeaseTemplateResponseDto> PublishTemplateAsync(PublishLeaseTemplateRequest request, int? userId)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Name)) request.Name = "StandardLeaseAgreement";
            if (string.IsNullOrWhiteSpace(request.ContentHtml))
                throw new InvalidOperationException("Template content HTML cannot be empty.");

            // Sanitize incoming HTML tags
            string sanitizedHtml = _sanitizer.Sanitize(request.ContentHtml);

            var row = await _db.PublishLeaseTemplateDbAsync(request.Name, sanitizedHtml, userId);

            return new LeaseTemplateResponseDto
            {
                Id = Convert.ToInt32(row["Id"]),
                Name = row["Name"]?.ToString() ?? request.Name,
                ContentHtml = row["ContentHtml"]?.ToString() ?? sanitizedHtml,
                IsActive = Convert.ToBoolean(row["IsActive"]),
                CreatedAt = Convert.ToDateTime(row["CreatedAt"]),
                CreatedBy = row.TryGetValue("CreatedBy", out var cb) && cb != null && cb != DBNull.Value ? Convert.ToInt32(cb) : userId,
                CreatedByName = null
            };
        }
    }
}
