using System;

namespace WorkNest.Application.DTOs.Agreement
{
    public class PublishLeaseTemplateRequest
    {
        public string Name { get; set; } = "StandardLeaseAgreement";
        public string ContentHtml { get; set; } = string.Empty;
    }

    public class LeaseTemplateResponseDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ContentHtml { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
    }

    public class GenerateLeaseAgreementRequest : SendAgreementRequest
    {
        public string TemplateName { get; set; } = "StandardLeaseAgreement";
    }
}
