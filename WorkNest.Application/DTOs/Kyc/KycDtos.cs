using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using WorkNest.Domain.Entities;

namespace WorkNest.Application.DTOs.Kyc
{
    public static class KycCategoryMapper
    {
        public static (string Category, bool IsUnrecognized, string? WarningMessage) ResolveCategory(string? companyOrType)
        {
            if (string.IsNullOrWhiteSpace(companyOrType))
            {
                return (CustomerCategory.Individual, false, null);
            }

            var trimmed = companyOrType.Trim();

            if (string.Equals(trimmed, "Individual", StringComparison.OrdinalIgnoreCase))
            {
                return (CustomerCategory.Individual, false, null);
            }

            if (string.Equals(trimmed, "Company", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "AOP", StringComparison.OrdinalIgnoreCase))
            {
                return (CustomerCategory.Business, false, null);
            }

            // If company name is provided (e.g., "Tech Solutions Ltd"), it is a Business entity
            return (CustomerCategory.Business, false, null);
        }
    }

    public class KycCustomerListItemViewModel
    {
        public int Id { get; set; }
        public Guid IdGUID { get; set; }
        public string Code { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Company { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string Category { get; set; } = CustomerCategory.Individual;
        public int TotalActiveDocs { get; set; }
        public int VerifiedDocsCount { get; set; }
        public int PendingDocsCount { get; set; }
        public int RejectedDocsCount { get; set; }
        public int CompletenessPercentage { get; set; }
        public string CompletenessStatus { get; set; } = "Incomplete";
        public string OverallStatus { get; set; } = "Pending";
        public string OverallBadgeClass { get; set; } = "badge-secondary";
        public DateTime CreatedAt { get; set; }
    }

    public class KycDocumentTypeDropdownOption
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DisplayLabel { get; set; } = string.Empty;
        public string? GroupCode { get; set; }
        public string? AlternativeCode { get; set; }
        public bool AllowMultiple { get; set; }
        public bool RequiresHolderName { get; set; }
        public bool RequiresExpiry { get; set; }
        public bool IsRequired { get; set; }
        public bool IsUploaded { get; set; }
        public int? ExistingDocumentId { get; set; }
        public int SortOrder { get; set; }
    }

    public class KycPersonSlotOption
    {
        public byte SlotNo { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? HolderName { get; set; }
    }

    public class KycChecklistItemViewModel
    {
        public int DocumentTypeId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public byte RequirementLevel { get; set; }
        public bool IsUploaded { get; set; }
        public byte? Status { get; set; }
        public string? StatusLabel { get; set; }
        public string? StatusBadgeClass { get; set; }
        public int? DocumentId { get; set; }
        public byte SlotNo { get; set; } = 1;
        public string? HolderName { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }

    public class KycChecklistGroupViewModel
    {
        public string? GroupCode { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public bool IsSatisfied { get; set; }
        public string StatusSummary { get; set; } = string.Empty;
        public List<KycChecklistItemViewModel> Items { get; set; } = new();
    }

    public class CustomerKycPageViewModel
    {
        public int CustomerId { get; set; }
        public Guid CustomerGuid { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Company { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? CnicOrPassport { get; set; }
        public string? Address { get; set; }
        public string? CityName { get; set; }
        public string Category { get; set; } = CustomerCategory.Individual;
        public bool IsCategoryUnrecognized { get; set; }
        public string? WarningMessage { get; set; }
        public string FolderName { get; set; } = string.Empty;
        public bool CanVerifyAndReject { get; set; }
        public int CompletenessPercentage { get; set; }
        public string OverallStatus { get; set; } = "Pending";
        public string OverallBadgeClass { get; set; } = "badge-secondary";

        public List<KycDocumentTypeDropdownOption> AvailableDocumentTypes { get; set; } = new();
        public List<KycPersonSlotOption> ExistingPersonSlots { get; set; } = new();
        public List<KycChecklistGroupViewModel> ChecklistGroups { get; set; } = new();
        public List<CustomerKYCDocument> UploadedDocuments { get; set; } = new();
    }

    public class UploadKycDocumentDto
    {
        public int CustomerId { get; set; }
        public int DocumentTypeId { get; set; }
        public byte SlotNo { get; set; } = 1;
        public string? HolderName { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public IFormFile? File { get; set; }
    }

    public class VerifyKycDocumentDto
    {
        public int DocumentId { get; set; }
        public byte Status { get; set; } // 1 Verified, 2 Rejected
        public string? Remarks { get; set; }
    }
}
