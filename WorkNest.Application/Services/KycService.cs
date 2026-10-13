using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using WorkNest.Application.DTOs.Kyc;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Configurations;
using WorkNest.Common.Constants;
using WorkNest.Domain.Entities;

namespace WorkNest.Application.Services
{
    public class KycService : IKycService
    {
        private readonly IDbRepository _db;
        private readonly IKycFileStorage _fileStorage;
        private readonly KycStorageSettings _settings;
        private readonly IRecordScopeRepository _scope;

        public KycService(
            IDbRepository db,
            IKycFileStorage fileStorage,
            IOptions<KycStorageSettings> settings,
            IRecordScopeRepository scope)
        {
            _db = db;
            _fileStorage = fileStorage;
            _settings = settings.Value;
            _scope = scope;
        }

        public async Task<(IEnumerable<KycCustomerListItemViewModel> Items, int Total)> GetCustomerKycListAsync(
            int page,
            int limit,
            string? search,
            IReadOnlyCollection<int>? locationIds)
        {
            // A location-bound caller with no location assigned sees nobody (it used to mean "every location").
            if (locationIds is { Count: 0 }) return (Enumerable.Empty<KycCustomerListItemViewModel>(), 0);

            IEnumerable<KycCustomerListItemViewModel> rows;
            int total;
            if (locationIds == null || locationIds.Count == 1)
            {
                (rows, total) = await _db.GetCustomersKycListDbAsync(page, limit, search, locationIds?.First());
            }
            else
            {
                // The list procedure takes one location: read each location's customers in full, merge them (a customer
                // can be in several) and page here, newest first like the procedure.
                var merged = new Dictionary<int, KycCustomerListItemViewModel>();
                foreach (var locationId in locationIds.Distinct())
                    foreach (var row in (await _db.GetCustomersKycListDbAsync(1, int.MaxValue, search, locationId)).Rows)
                        merged.TryAdd(row.Id, row);
                int size = limit < 1 ? 20 : limit;
                total = merged.Count;
                rows = merged.Values.OrderByDescending(c => c.Id).Skip((Math.Max(page, 1) - 1) * size).Take(size).ToList();
            }

            // Calculate completeness percentage and status dynamically for each customer
            var activeTypes = (await _db.GetActiveKycDocumentTypesDbAsync(null)).ToList();

            var resultList = new List<KycCustomerListItemViewModel>();
            foreach (var customer in rows)
            {
                var docs = (await _db.GetCustomerKycDocumentsDbAsync(customer.Id, includeInactive: false)).ToList();
                var (percent, status) = EvaluateCustomerCompleteness(customer.Category, activeTypes, docs);
                customer.CompletenessPercentage = percent;
                customer.CompletenessStatus = status;
                resultList.Add(customer);
            }

            return (resultList, total);
        }

        public async Task<CustomerKycPageViewModel?> GetCustomerKycPortalAsync(
            string customerIdOrGuid,
            IReadOnlyCollection<int>? locationIds,
            bool canVerifyAndReject)
        {
            var customer = await _db.GetCustomerByIdOrGuidDbAsync(customerIdOrGuid);
            if (customer == null) return null;

            int customerId = Convert.ToInt32(customer["Id"]);

            // Location Scoping Check (answers like a missing customer)
            if (!await ReachesCustomerAsync(customerId, locationIds)) return null;

            string? company = customer.TryGetValue("Company", out var comp) && comp != null ? comp.ToString() : null;
            var (category, isUnrecognized, warningMessage) = KycCategoryMapper.ResolveCategory(company);

            var activeDocTypes = (await _db.GetActiveKycDocumentTypesDbAsync(category)).ToList();
            var uploadedDocs = (await _db.GetCustomerKycDocumentsDbAsync(customerId, includeInactive: false)).ToList();

            // Folder Name
            string folderName = uploadedDocs.FirstOrDefault()?.FolderName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(folderName))
            {
                string custCode = customer["Code"]?.ToString() ?? $"WN{customerId:D5}";
                string shortGuid = Guid.NewGuid().ToString("N")[..12].ToLowerInvariant();
                folderName = $"{custCode}_{shortGuid}";
            }

            // Build Dropdown Options
            var dropdownOptions = BuildDropdownOptions(activeDocTypes, uploadedDocs, category);

            // Build Person Slots (for AllowMultiple / PERSON_IDENTITY)
            var personSlots = BuildPersonSlots(uploadedDocs);

            // Build Checklist Groups
            var checklistGroups = BuildChecklist(activeDocTypes, uploadedDocs, category);

            // Evaluate Overall Completeness
            var (completenessPercent, _) = EvaluateCustomerCompleteness(category, activeDocTypes, uploadedDocs);

            // Overall Status
            string overallStatus = "Pending Upload";
            string overallBadgeClass = "badge-secondary";
            if (uploadedDocs.Count > 0)
            {
                if (uploadedDocs.Any(d => d.Status == KycDocumentStatus.Rejected))
                {
                    overallStatus = "Action Required (Rejected Documents)";
                    overallBadgeClass = "badge-danger";
                }
                else if (uploadedDocs.Any(d => d.Status == KycDocumentStatus.Pending))
                {
                    overallStatus = "Under Review (Pending Verification)";
                    overallBadgeClass = "badge-warning";
                }
                else if (uploadedDocs.All(d => d.Status == KycDocumentStatus.Verified) && completenessPercent == 100)
                {
                    overallStatus = "Fully Verified";
                    overallBadgeClass = "badge-success";
                }
                else if (uploadedDocs.All(d => d.Status == KycDocumentStatus.Verified))
                {
                    overallStatus = "Partially Verified";
                    overallBadgeClass = "badge-info";
                }
            }

            return new CustomerKycPageViewModel
            {
                CustomerId = customerId,
                CustomerGuid = customer.TryGetValue("IdGUID", out var g) && g is Guid guidVal ? guidVal : Guid.Empty,
                CustomerCode = customer["Code"]?.ToString() ?? $"WN{customerId:D5}",
                FullName = customer["FullName"]?.ToString() ?? "",
                Company = company,
                Email = customer["Email"]?.ToString() ?? "",
                PhoneNumber = customer.TryGetValue("PhoneNumber", out var ph) && ph != null ? ph.ToString() : null,
                CnicOrPassport = customer.TryGetValue("CnicOrPassport", out var cnic) && cnic != null ? cnic.ToString() : null,
                Address = customer.TryGetValue("Address", out var addr) && addr != null ? addr.ToString() : null,
                CityName = customer.TryGetValue("CityName", out var city) && city != null ? city.ToString() : null,
                Category = category,
                IsCategoryUnrecognized = isUnrecognized,
                WarningMessage = warningMessage,
                FolderName = folderName,
                CanVerifyAndReject = canVerifyAndReject,
                CompletenessPercentage = completenessPercent,
                OverallStatus = overallStatus,
                OverallBadgeClass = overallBadgeClass,
                AvailableDocumentTypes = dropdownOptions,
                ExistingPersonSlots = personSlots,
                ChecklistGroups = checklistGroups,
                UploadedDocuments = uploadedDocs
            };
        }

        public async Task<(bool Success, string Message, int? DocumentId)> UploadOrReplaceDocumentAsync(
            int customerId,
            int documentTypeId,
            byte slotNo,
            string? holderName,
            DateTime? expiryDate,
            IFormFile file,
            int uploadedByUserId,
            IReadOnlyCollection<int>? locationIds)
        {
            if (file == null || file.Length == 0)
            {
                return (false, "Please select a valid file to upload.", null);
            }

            // Location Scoping Check
            if (!await ReachesCustomerAsync(customerId, locationIds))
            {
                return (false, "Access denied: Customer does not belong to your assigned location.", null);
            }

            var customer = await _db.GetCustomerByIdOrGuidDbAsync(customerId.ToString());
            if (customer == null)
            {
                return (false, "Customer not found.", null);
            }

            string? company = customer.TryGetValue("Company", out var comp) && comp != null ? comp.ToString() : null;
            var (category, _, _) = KycCategoryMapper.ResolveCategory(company);

            // Validate Document Type Server-Side
            var activeTypes = await _db.GetActiveKycDocumentTypesDbAsync(category);
            var docType = activeTypes.FirstOrDefault(t => t.Id == documentTypeId);
            if (docType == null)
            {
                return (false, "Selected document type is not applicable for this customer category or is inactive.", null);
            }

            // Validate Holder Name if required
            if ((docType.RequiresHolderName || docType.AllowMultiple) && string.IsNullOrWhiteSpace(holderName))
            {
                return (false, $"Holder Name is required for {docType.Name}.", null);
            }

            // Validate Expiry Date if required
            if (docType.RequiresExpiry && !expiryDate.HasValue)
            {
                return (false, $"Expiry Date is required for {docType.Name}.", null);
            }

            // File Size Validation
            if (file.Length > _settings.MaxFileSizeBytes)
            {
                return (false, $"File size exceeds the maximum allowed limit of {_settings.MaxFileSizeBytes / (1024 * 1024)} MB.", null);
            }

            // File Extension Validation
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_settings.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                return (false, $"File type '{ext}' is not allowed. Allowed formats: {string.Join(", ", _settings.AllowedExtensions)}.", null);
            }

            // Read Stream & Validate Magic Bytes + Compute SHA-256
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            ms.Seek(0, SeekOrigin.Begin);

            if (!ValidateFileSignature(ms, ext))
            {
                return (false, "File signature verification failed. The file content does not match its extension.", null);
            }

            ms.Seek(0, SeekOrigin.Begin);
            string fileHash = ComputeSha256(ms);
            ms.Seek(0, SeekOrigin.Begin);

            // Resolve Customer Code & Folder Name
            string customerCode = customer["Code"]?.ToString() ?? $"WN{customerId:D5}";
            var existingDocs = await _db.GetCustomerKycDocumentsDbAsync(customerId, includeInactive: true);
            string folderName = existingDocs.FirstOrDefault()?.FolderName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(folderName))
            {
                string shortGuid = Guid.NewGuid().ToString("N")[..12].ToLowerInvariant();
                folderName = $"{customerCode}_{shortGuid}";
            }

            // Construct sanitized unique physical file name
            string fileGuid = Guid.NewGuid().ToString("N")[..12].ToLowerInvariant();
            string sanitizedDocCode = docType.Code.Replace("+", "_");
            string physicalFileName = $"{customerCode}_{sanitizedDocCode}_{fileGuid}{ext}";

            string storedPath = string.Empty;
            try
            {
                // 1. Save file to disk first
                storedPath = await _fileStorage.SaveAsync(folderName, physicalFileName, ms);

                // 2. Perform DB Replace/Insert transaction
                int newDocId = await _db.InsertOrReplaceCustomerKycDocumentDbAsync(
                    customerId,
                    folderName,
                    documentTypeId,
                    slotNo,
                    holderName?.Trim(),
                    storedPath,
                    file.FileName,
                    fileHash,
                    expiryDate,
                    uploadedByUserId
                );

                return (true, "Document uploaded successfully.", newDocId);
            }
            catch (Exception ex)
            {
                // If DB fails, delete the newly saved physical file
                if (!string.IsNullOrWhiteSpace(storedPath))
                {
                    await _fileStorage.DeleteAsync(storedPath);
                }
                return (false, $"Upload failed: {ex.Message}", null);
            }
        }

        public async Task<(bool Success, string Message)> VerifyOrRejectDocumentAsync(
            int documentId,
            byte status,
            string? remarks,
            int verifiedByUserId,
            string userRole,
            IReadOnlyCollection<int>? locationIds)
        {
            // Strict role-based enforcement in service layer: Admin or SuperAdmin ONLY
            if (!Roles.IsAdminRole(userRole))
            {
                return (false, "Access denied: Only Admins and SuperAdmins can verify or reject KYC documents.");
            }

            if (status == KycDocumentStatus.Rejected && string.IsNullOrWhiteSpace(remarks))
            {
                return (false, "Remarks are mandatory when rejecting a document.");
            }

            var doc = await _db.GetCustomerKycDocumentByIdDbAsync(documentId);
            if (doc == null)
            {
                return (false, "KYC Document not found.");
            }

            // Location Scoping Check
            if (!await ReachesCustomerAsync(doc.CustomerId, locationIds))
            {
                return (false, "Access denied: Document belongs to a customer outside your assigned location.");
            }

            await _db.SetCustomerKycDocumentStatusDbAsync(documentId, status, remarks?.Trim(), verifiedByUserId);
            return (true, status == KycDocumentStatus.Verified ? "Document verified successfully." : "Document rejected.");
        }

        public async Task<(Stream? Stream, string ContentType, string FileName, string Error)> DownloadDocumentAsync(
            int documentId,
            IReadOnlyCollection<int>? locationIds)
        {
            var doc = await _db.GetCustomerKycDocumentByIdDbAsync(documentId);
            if (doc == null)
            {
                return (null, string.Empty, string.Empty, "Document not found.");
            }

            // Location Scoping Check
            if (!await ReachesCustomerAsync(doc.CustomerId, locationIds))
            {
                return (null, string.Empty, string.Empty, "Access denied: Customer does not belong to your assigned location.");
            }

            var stream = await _fileStorage.OpenReadAsync(doc.StoredPath);
            if (stream == null)
            {
                return (null, string.Empty, string.Empty, "Physical document file not found on storage.");
            }

            var ext = Path.GetExtension(doc.StoredPath).ToLowerInvariant();
            string contentType = ext switch
            {
                ".pdf" => "application/pdf",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                _ => "application/octet-stream"
            };

            return (stream, contentType, doc.OriginalFileName, string.Empty);
        }

        public async Task<IEnumerable<CustomerKYCDocument>?> GetDocumentHistoryAsync(
            int customerId,
            int documentTypeId,
            byte slotNo,
            IReadOnlyCollection<int>? locationIds)
        {
            if (!await ReachesCustomerAsync(customerId, locationIds)) return null;

            var allDocs = await _db.GetCustomerKycDocumentsDbAsync(customerId, includeInactive: true);
            return allDocs
                .Where(d => d.DocumentTypeId == documentTypeId && d.SlotNo == slotNo)
                .OrderByDescending(d => d.VersionNo);
        }

        /// <summary>
        /// Same rule as [RecordScope] on GET customer/{id}: null <paramref name="locationIds"/> (not location-bound)
        /// reaches every customer; a customer with no location yet (no booking and no location on their user account)
        /// is open to location-bound staff too; otherwise one of the customer's locations (bookings or user account)
        /// must be one of the caller's. A customer tied only to other locations is refused.
        /// </summary>
        private async Task<bool> ReachesCustomerAsync(int customerId, IReadOnlyCollection<int>? locationIds)
        {
            if (locationIds == null) return true;
            var customerLocations = await _scope.GetLocationIdsAsync(RecordKind.Customer, customerId.ToString());
            return customerLocations == null || customerLocations.Count == 0 || customerLocations.Any(locationIds.Contains);
        }

        #region Helper Validation & Solvers

        private static bool ValidateFileSignature(Stream stream, string extension)
        {
            byte[] header = new byte[8];
            int read = stream.Read(header, 0, header.Length);
            if (read < 3) return false;

            return extension switch
            {
                ".pdf" => header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46, // %PDF
                ".jpg" or ".jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,           // JPEG SOI
                ".png" => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47, // PNG header
                _ => false
            };
        }

        private static string ComputeSha256(Stream stream)
        {
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(stream);
            var sb = new StringBuilder(64);
            foreach (var b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        private static List<KycDocumentTypeDropdownOption> BuildDropdownOptions(
            List<KYCDocumentType> activeDocTypes,
            List<CustomerKYCDocument> uploadedDocs,
            string category)
        {
            var options = new List<KycDocumentTypeDropdownOption>();
            var groupedTypes = activeDocTypes.Where(t => !string.IsNullOrWhiteSpace(t.GroupCode)).GroupBy(t => t.GroupCode!);

            // Map each active document type
            foreach (var type in activeDocTypes.OrderBy(t => t.SortOrder))
            {
                byte req = category == CustomerCategory.Individual ? type.IndividualRequirement : type.BusinessRequirement;
                bool isRequired = req == KycRequirementLevel.Required;

                string displayLabel = type.Name;
                if (!string.IsNullOrWhiteSpace(type.GroupCode))
                {
                    // Generic label generation from alternative codes within the group
                    var groupPeers = activeDocTypes.Where(t => t.GroupCode == type.GroupCode).ToList();
                    var distinctAlts = groupPeers.Select(p => p.AlternativeCode).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList();
                    if (distinctAlts.Count > 1)
                    {
                        displayLabel = $"{type.Name} — {type.GroupCode}: {string.Join(" or ", distinctAlts)}";
                    }
                }

                bool alreadyUploaded = !type.AllowMultiple && uploadedDocs.Any(d => d.DocumentTypeId == type.Id && d.IsActive);
                if (alreadyUploaded)
                {
                    displayLabel += " (uploaded — replace)";
                }

                var existingDoc = uploadedDocs.FirstOrDefault(d => d.DocumentTypeId == type.Id && d.IsActive);

                options.Add(new KycDocumentTypeDropdownOption
                {
                    Id = type.Id,
                    Code = type.Code,
                    Name = type.Name,
                    DisplayLabel = displayLabel,
                    GroupCode = type.GroupCode,
                    AlternativeCode = type.AlternativeCode,
                    AllowMultiple = type.AllowMultiple,
                    RequiresHolderName = type.RequiresHolderName,
                    RequiresExpiry = type.RequiresExpiry,
                    IsRequired = isRequired,
                    IsUploaded = alreadyUploaded,
                    ExistingDocumentId = existingDoc?.Id,
                    SortOrder = type.SortOrder
                });
            }

            return options;
        }

        private static List<KycPersonSlotOption> BuildPersonSlots(List<CustomerKYCDocument> uploadedDocs)
        {
            var slots = new List<KycPersonSlotOption>();
            var personGroups = uploadedDocs
                .Where(d => d.AllowMultiple)
                .GroupBy(d => d.SlotNo)
                .OrderBy(g => g.Key);

            foreach (var g in personGroups)
            {
                string holderName = g.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.HolderName))?.HolderName ?? $"Person #{g.Key}";
                slots.Add(new KycPersonSlotOption
                {
                    SlotNo = g.Key,
                    DisplayName = $"Person {g.Key}: {holderName}",
                    HolderName = holderName
                });
            }

            return slots;
        }

        private static List<KycChecklistGroupViewModel> BuildChecklist(
            List<KYCDocumentType> activeDocTypes,
            List<CustomerKYCDocument> uploadedDocs,
            string category)
        {
            var checklist = new List<KycChecklistGroupViewModel>();

            // 1. Ungrouped Required & Optional Types
            var ungroupedTypes = activeDocTypes.Where(t => string.IsNullOrWhiteSpace(t.GroupCode)).OrderBy(t => t.SortOrder);
            foreach (var type in ungroupedTypes)
            {
                byte req = category == CustomerCategory.Individual ? type.IndividualRequirement : type.BusinessRequirement;
                var doc = uploadedDocs.FirstOrDefault(d => d.DocumentTypeId == type.Id && d.IsActive);

                bool isUploaded = doc != null;
                bool isRequired = req == KycRequirementLevel.Required;

                checklist.Add(new KycChecklistGroupViewModel
                {
                    GroupCode = null,
                    Title = type.Name + (isRequired ? " (Required)" : " (Optional)"),
                    IsRequired = isRequired,
                    IsSatisfied = isUploaded || !isRequired,
                    StatusSummary = isUploaded ? $"Uploaded ({KycDocumentStatus.GetLabel(doc!.Status)})" : (isRequired ? "Missing" : "Optional"),
                    Items = new List<KycChecklistItemViewModel>
                    {
                        new KycChecklistItemViewModel
                        {
                            DocumentTypeId = type.Id,
                            Code = type.Code,
                            Name = type.Name,
                            RequirementLevel = req,
                            IsUploaded = isUploaded,
                            Status = doc?.Status,
                            StatusLabel = doc != null ? KycDocumentStatus.GetLabel(doc.Status) : "Missing",
                            StatusBadgeClass = doc != null ? KycDocumentStatus.GetBadgeClass(doc.Status) : "badge-secondary",
                            DocumentId = doc?.Id,
                            SlotNo = doc?.SlotNo ?? 1,
                            HolderName = doc?.HolderName,
                            ExpiryDate = doc?.ExpiryDate
                        }
                    }
                });
            }

            // 2. Grouped Either/Or Types (Generic solver)
            var groups = activeDocTypes.Where(t => !string.IsNullOrWhiteSpace(t.GroupCode)).GroupBy(t => t.GroupCode!);
            foreach (var grp in groups)
            {
                var groupTypes = grp.OrderBy(t => t.SortOrder).ToList();
                bool isGroupRequired = groupTypes.Any(t => (category == CustomerCategory.Individual ? t.IndividualRequirement : t.BusinessRequirement) == KycRequirementLevel.Required);
                bool allowMultiple = groupTypes.Any(t => t.AllowMultiple);

                var distinctAlts = groupTypes.Select(t => t.AlternativeCode).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList();
                string groupTitle = $"{grp.Key}: {string.Join(" or ", distinctAlts)}" + (isGroupRequired ? " (Required)" : " (Optional)");

                bool isGroupSatisfied = false;
                string statusSummary = "";

                if (!allowMultiple)
                {
                    // Single slot evaluation
                    var satisfiedAlts = new List<string>();
                    var partialSummaries = new List<string>();

                    foreach (var alt in distinctAlts)
                    {
                        var altTypes = groupTypes.Where(t => t.AlternativeCode == alt).ToList();
                        var uploadedAltDocs = altTypes.Where(t => uploadedDocs.Any(d => d.DocumentTypeId == t.Id && d.IsActive)).ToList();

                        if (uploadedAltDocs.Count == altTypes.Count)
                        {
                            satisfiedAlts.Add(alt!);
                        }
                        else if (uploadedAltDocs.Count > 0)
                        {
                            var missingNames = altTypes.Except(uploadedAltDocs).Select(t => t.Name);
                            var uploadedNames = uploadedAltDocs.Select(t => t.Name);
                            partialSummaries.Add($"{string.Join(" & ", uploadedNames)} uploaded, {string.Join(" & ", missingNames)} missing");
                        }
                    }

                    if (satisfiedAlts.Count > 0)
                    {
                        isGroupSatisfied = true;
                        statusSummary = $"Satisfied via {string.Join(" / ", satisfiedAlts)}";
                    }
                    else if (partialSummaries.Count > 0)
                    {
                        statusSummary = string.Join("; ", partialSummaries);
                    }
                    else
                    {
                        statusSummary = isGroupRequired ? "Missing" : "Optional";
                    }
                }
                else
                {
                    // Multi-slot person evaluation (SlotNo)
                    var distinctSlots = uploadedDocs.Where(d => groupTypes.Any(t => t.Id == d.DocumentTypeId) && d.IsActive).Select(d => d.SlotNo).Distinct().ToList();
                    var satisfiedPersons = new List<string>();
                    var partialSummaries = new List<string>();

                    foreach (var slot in distinctSlots)
                    {
                        var slotDocs = uploadedDocs.Where(d => d.SlotNo == slot && d.IsActive).ToList();
                        string personLabel = slotDocs.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.HolderName))?.HolderName ?? $"Person #{slot}";

                        foreach (var alt in distinctAlts)
                        {
                            var altTypes = groupTypes.Where(t => t.AlternativeCode == alt).ToList();
                            var uploadedForAlt = altTypes.Where(t => slotDocs.Any(d => d.DocumentTypeId == t.Id)).ToList();

                            if (uploadedForAlt.Count == altTypes.Count)
                            {
                                satisfiedPersons.Add($"{personLabel} ({alt})");
                            }
                            else if (uploadedForAlt.Count > 0)
                            {
                                var missing = altTypes.Except(uploadedForAlt).Select(t => t.Name);
                                partialSummaries.Add($"{personLabel}: {string.Join(" & ", missing)} missing");
                            }
                        }
                    }

                    if (satisfiedPersons.Count > 0)
                    {
                        isGroupSatisfied = true;
                        statusSummary = $"Satisfied: {string.Join(", ", satisfiedPersons)}";
                    }
                    else if (partialSummaries.Count > 0)
                    {
                        statusSummary = string.Join("; ", partialSummaries);
                    }
                    else
                    {
                        statusSummary = isGroupRequired ? "Missing (At least 1 person required)" : "Optional";
                    }
                }

                var items = new List<KycChecklistItemViewModel>();
                foreach (var type in groupTypes)
                {
                    var matchingDocs = uploadedDocs.Where(d => d.DocumentTypeId == type.Id && d.IsActive).ToList();
                    byte req = category == CustomerCategory.Individual ? type.IndividualRequirement : type.BusinessRequirement;

                    if (matchingDocs.Count > 0)
                    {
                        foreach (var d in matchingDocs)
                        {
                            items.Add(new KycChecklistItemViewModel
                            {
                                DocumentTypeId = type.Id,
                                Code = type.Code,
                                Name = type.Name + (!string.IsNullOrWhiteSpace(d.HolderName) ? $" ({d.HolderName})" : ""),
                                RequirementLevel = req,
                                IsUploaded = true,
                                Status = d.Status,
                                StatusLabel = KycDocumentStatus.GetLabel(d.Status),
                                StatusBadgeClass = KycDocumentStatus.GetBadgeClass(d.Status),
                                DocumentId = d.Id,
                                SlotNo = d.SlotNo,
                                HolderName = d.HolderName,
                                ExpiryDate = d.ExpiryDate
                            });
                        }
                    }
                    else
                    {
                        items.Add(new KycChecklistItemViewModel
                        {
                            DocumentTypeId = type.Id,
                            Code = type.Code,
                            Name = type.Name,
                            RequirementLevel = req,
                            IsUploaded = false,
                            Status = null,
                            StatusLabel = "Missing",
                            StatusBadgeClass = "badge-secondary",
                            DocumentId = null,
                            SlotNo = 1,
                            HolderName = null,
                            ExpiryDate = null
                        });
                    }
                }

                checklist.Add(new KycChecklistGroupViewModel
                {
                    GroupCode = grp.Key,
                    Title = groupTitle,
                    IsRequired = isGroupRequired,
                    IsSatisfied = isGroupSatisfied || !isGroupRequired,
                    StatusSummary = statusSummary,
                    Items = items
                });
            }

            return checklist;
        }

        private static (int Percentage, string Status) EvaluateCustomerCompleteness(
            string category,
            IEnumerable<KYCDocumentType> activeTypes,
            IEnumerable<CustomerKYCDocument> uploadedDocs)
        {
            var docsList = uploadedDocs.ToList();
            var applicableTypes = activeTypes.Where(t => (category == CustomerCategory.Individual ? t.IndividualRequirement : t.BusinessRequirement) > 0).ToList();
            if (applicableTypes.Count == 0) return (100, "Complete");

            // Required Ungrouped Types
            var requiredUngrouped = applicableTypes.Where(t => string.IsNullOrWhiteSpace(t.GroupCode) && (category == CustomerCategory.Individual ? t.IndividualRequirement : t.BusinessRequirement) == KycRequirementLevel.Required).ToList();
            int totalChecklistUnits = requiredUngrouped.Count;
            int satisfiedUnits = 0;

            foreach (var type in requiredUngrouped)
            {
                if (docsList.Any(d => d.DocumentTypeId == type.Id && d.IsActive))
                {
                    satisfiedUnits++;
                }
            }

            // Required Grouped Types
            var requiredGroups = applicableTypes.Where(t => !string.IsNullOrWhiteSpace(t.GroupCode) && (category == CustomerCategory.Individual ? t.IndividualRequirement : t.BusinessRequirement) == KycRequirementLevel.Required).GroupBy(t => t.GroupCode!).ToList();

            totalChecklistUnits += requiredGroups.Count;

            foreach (var grp in requiredGroups)
            {
                var groupTypes = grp.ToList();
                bool allowMultiple = groupTypes.Any(t => t.AllowMultiple);
                var distinctAlts = groupTypes.Select(t => t.AlternativeCode).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList();

                bool satisfied = false;
                if (!allowMultiple)
                {
                    foreach (var alt in distinctAlts)
                    {
                        var altTypes = groupTypes.Where(t => t.AlternativeCode == alt).ToList();
                        if (altTypes.All(t => docsList.Any(d => d.DocumentTypeId == t.Id && d.IsActive)))
                        {
                            satisfied = true;
                            break;
                        }
                    }
                }
                else
                {
                    var distinctSlots = docsList.Where(d => groupTypes.Any(t => t.Id == d.DocumentTypeId) && d.IsActive).Select(d => d.SlotNo).Distinct().ToList();
                    foreach (var slot in distinctSlots)
                    {
                        var slotDocs = docsList.Where(d => d.SlotNo == slot && d.IsActive).ToList();
                        foreach (var alt in distinctAlts)
                        {
                            var altTypes = groupTypes.Where(t => t.AlternativeCode == alt).ToList();
                            if (altTypes.All(t => slotDocs.Any(d => d.DocumentTypeId == t.Id)))
                            {
                                satisfied = true;
                                break;
                            }
                        }
                        if (satisfied) break;
                    }
                }

                if (satisfied)
                {
                    satisfiedUnits++;
                }
            }

            if (totalChecklistUnits == 0)
            {
                return (docsList.Count > 0 ? 100 : 0, docsList.Count > 0 ? "Complete" : "Incomplete");
            }

            int percent = (int)Math.Round((double)satisfiedUnits / totalChecklistUnits * 100.0);
            string status = percent >= 100 ? "Complete" : "Incomplete";

            return (percent, status);
        }

        #endregion
    }
}
