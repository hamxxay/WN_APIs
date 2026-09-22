using System;

namespace WorkNest.Domain.Entities
{
    public class Agreement
    {
        public int Id { get; set; }
        public Guid Guid { get; set; } = Guid.NewGuid();
        public int QuotationId { get; set; }
        public int? BookingId { get; set; }
        public int CustomerId { get; set; }
        public string EntityType { get; set; } = "Individual";
        public string Status { get; set; } = "AgreementSent";
        public DateTime SentDate { get; set; } = DateTime.UtcNow;
        public DateTime? SignedDate { get; set; }
        public int RefundDays { get; set; } = 30;
        public decimal FeeAmount { get; set; }
        public decimal SecurityDeposit { get; set; }
        public string? OperatingHours { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerCnic { get; set; }
        public string? CustomerPhone { get; set; }
        public string? CustomerAddress { get; set; }
        public string? CompanyName { get; set; }
        public string? Ntn { get; set; }
        public string? SecpRegistrationNo { get; set; }
        public int? TemplateVersionId { get; set; }
        public string? SignedPdfPath { get; set; }
        public DateTime? SignedPdfUploadedAt { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedOn { get; set; }
    }
}
