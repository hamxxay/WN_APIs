using System;

namespace WorkNest.Domain.Entities
{
    public static class KycRequirementLevel
    {
        public const byte NotApplicable = 0;
        public const byte Optional = 1;
        public const byte Required = 2;
    }

    public static class KycDocumentStatus
    {
        public const byte Pending = 0;
        public const byte Verified = 1;
        public const byte Rejected = 2;

        public static string GetLabel(byte status) => status switch
        {
            Verified => "Verified",
            Rejected => "Rejected",
            _ => "Pending"
        };

        public static string GetBadgeClass(byte status) => status switch
        {
            Verified => "badge-success",
            Rejected => "badge-danger",
            _ => "badge-warning"
        };
    }

    public static class CustomerCategory
    {
        public const string Individual = "Individual";
        public const string Business = "Business";
    }

    public class KYCDocumentType
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public byte IndividualRequirement { get; set; }
        public byte BusinessRequirement { get; set; }
        public string? GroupCode { get; set; }
        public string? AlternativeCode { get; set; }
        public bool AllowMultiple { get; set; }
        public bool RequiresHolderName { get; set; }
        public bool RequiresExpiry { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class CustomerKYCDocument
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string FolderName { get; set; } = string.Empty;
        public int DocumentTypeId { get; set; }
        public string? DocumentTypeCode { get; set; }
        public string? DocumentTypeName { get; set; }
        public string? GroupCode { get; set; }
        public string? AlternativeCode { get; set; }
        public bool AllowMultiple { get; set; }
        public bool RequiresHolderName { get; set; }
        public bool RequiresExpiry { get; set; }
        public byte SlotNo { get; set; } = 1;
        public string? HolderName { get; set; }
        public string StoredPath { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string FileHash { get; set; } = string.Empty;
        public DateTime? ExpiryDate { get; set; }
        public int VersionNo { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public byte Status { get; set; } = KycDocumentStatus.Pending;
        public string? Remarks { get; set; }
        public int? VerifiedBy { get; set; }
        public string? VerifiedByName { get; set; }
        public DateTime? VerifiedOn { get; set; }
        public int UploadedBy { get; set; }
        public string? UploadedByName { get; set; }
        public DateTime UploadedOn { get; set; } = DateTime.UtcNow;
        public int? ReplacedBy { get; set; }
        public DateTime? ReplacedOn { get; set; }
    }
}
