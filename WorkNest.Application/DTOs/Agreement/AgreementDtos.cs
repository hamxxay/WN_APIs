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

        public string? CenterName { get; set; }
        public string? VendorLegalName { get; set; }
        public string? VendorAddress { get; set; }
        public string? VendorPhone { get; set; }
        public string? VendorNtn { get; set; }
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
        public string? SignedPdfPath { get; set; }
        public DateTime? SignedPdfUploadedAt { get; set; }
        public DateTime CreatedOn { get; set; }
        /// <summary>Signature reminders emailed since the agreement was sent (admin list only).</summary>
        public int ReminderCount { get; set; }
        /// <summary>When the last signature reminder was emailed (UTC, admin list only).</summary>
        public DateTime? LastReminderAt { get; set; }
        /// <summary>Signer name when the customer signed electronically (admin list only; null = not e-signed or unknown).</summary>
        public string? ESignedBy { get; set; }
        /// <summary>When the customer signed electronically, Pakistan time (admin list only).</summary>
        public DateTime? ESignedAt { get; set; }
    }

    public class MarkAgreementSignedRequest
    {
        public int AgreementId { get; set; }
        public int QuotationId { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>Customer portal: sign an agreement electronically (POST api/agreement/my/{id}/esign).</summary>
    public class ESignAgreementRequest
    {
        public string? SignerName { get; set; }
        /// <summary>The drawn or typed signature as "data:image/png;base64,...".</summary>
        public string? SignatureImage { get; set; }
        /// <summary>The customer ticked "I have read the agreement and agree to sign it electronically".</summary>
        public bool Consent { get; set; }
    }

    /// <summary>What is printed on the "Electronic signature certificate" page and stored in WN_AgreementESignatures.</summary>
    public class AgreementESignatureEvidence
    {
        public int AgreementId { get; set; }
        public string? QuotationNumber { get; set; }
        public string? CustomerName { get; set; }
        public string SignerName { get; set; } = "";
        public string? SignerEmail { get; set; }
        /// <summary>Pakistan time (IBusinessClock).</summary>
        public DateTime SignedAt { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        /// <summary>SHA-256 (hex, lower case) of the agreement PDF as presented for signing.</summary>
        public string DocumentSha256 { get; set; } = "";
        public byte[] SignatureImage { get; set; } = Array.Empty<byte>();
    }

    /// <summary>Staff view of an e-signature (GET api/agreement/{id}/esignature).</summary>
    public class AgreementESignatureDto
    {
        public int AgreementId { get; set; }
        public string? SignerName { get; set; }
        public string? SignerEmail { get; set; }
        public DateTime SignedAt { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? DocumentSha256 { get; set; }
        /// <summary>"data:image/png;base64,..." of the signature.</summary>
        public string? SignatureImage { get; set; }
    }

    /// <summary>Result of an e-signature: Completed = the booking was created in the same request.</summary>
    public class ESignAgreementResult
    {
        public int AgreementId { get; set; }
        public bool Completed { get; set; }
        public int? BookingId { get; set; }
        public string Status { get; set; } = "";
    }
}
