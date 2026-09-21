using System;

namespace WorkNest.Application.DTOs.Agreement
{
    public class SendAgreementRequest
    {
        public int QuotationId { get; set; }
        public string EntityType { get; set; } = "Individual"; // "Individual", "Company", "AOP"
        public string? FullName { get; set; }
        public string? Cnic { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public string? CompanyName { get; set; }
        public string? Ntn { get; set; }
        public string? SecpRegistrationNo { get; set; }
        public int RefundDays { get; set; } = 30;
        public decimal FeeAmount { get; set; }
        public decimal SecurityDeposit { get; set; }
        public DateTime? ContractStartDate { get; set; }
        public DateTime? ContractEndDate { get; set; }
        public string? OperatingHours { get; set; } = "24/7";
        public string? BillingFrequency { get; set; }
        public string? OverrideEmail { get; set; }
    }

    public class AgreementResponseDto
    {
        public int Id { get; set; }
        public string? Guid { get; set; }
        public int QuotationId { get; set; }
        public string? QuotationNumber { get; set; }
        public int? BookingId { get; set; }
        public int CustomerId { get; set; }
        public string EntityType { get; set; } = "Individual";
        public string Status { get; set; } = "AgreementSent";
        public DateTime SentDate { get; set; }
        public DateTime? SignedDate { get; set; }
        public int RefundDays { get; set; } = 30;
        public decimal FeeAmount { get; set; }
        public decimal SecurityDeposit { get; set; }
        public string? OperatingHours { get; set; }
        public string? DocumentUrl { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerCnic { get; set; }
        public string? CustomerPhone { get; set; }
        public string? CustomerAddress { get; set; }
        public string? CompanyName { get; set; }
        public string? Ntn { get; set; }
        public string? SecpRegistrationNo { get; set; }
        public int? TemplateVersionId { get; set; }
        public string? TemplateName { get; set; }
        public DateTime CreatedOn { get; set; }
    }

    public class MarkAgreementSignedRequest
    {
        public int AgreementId { get; set; }
        public int QuotationId { get; set; }
        public string? Note { get; set; }
    }
}
