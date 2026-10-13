using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using WorkNest.Application.DTOs.Kyc;
using WorkNest.Domain.Entities;

namespace WorkNest.Application.Interfaces
{
    public interface IKycService
    {
        /// <summary>
        /// <paramref name="locationIds"/> on every method: the caller's locations (main + extra) when they are location-bound,
        /// null for everyone else. Location-bound callers reach only customers of those locations; an empty list reaches nobody.
        /// </summary>
        Task<(IEnumerable<KycCustomerListItemViewModel> Items, int Total)> GetCustomerKycListAsync(int page, int limit, string? search, IReadOnlyCollection<int>? locationIds);
        
        /// <summary>Null when there is no such customer or the caller's locations don't reach them.</summary>
        Task<CustomerKycPageViewModel?> GetCustomerKycPortalAsync(string customerIdOrGuid, IReadOnlyCollection<int>? locationIds, bool canVerifyAndReject);
        
        Task<(bool Success, string Message, int? DocumentId)> UploadOrReplaceDocumentAsync(
            int customerId,
            int documentTypeId,
            byte slotNo,
            string? holderName,
            DateTime? expiryDate,
            IFormFile file,
            int uploadedByUserId,
            IReadOnlyCollection<int>? locationIds);

        Task<(bool Success, string Message)> VerifyOrRejectDocumentAsync(
            int documentId,
            byte status,
            string? remarks,
            int verifiedByUserId,
            string userRole,
            IReadOnlyCollection<int>? locationIds);

        Task<(Stream? Stream, string ContentType, string FileName, string Error)> DownloadDocumentAsync(
            int documentId,
            IReadOnlyCollection<int>? locationIds);

        /// <summary>Null when the caller's locations don't reach the customer.</summary>
        Task<IEnumerable<CustomerKYCDocument>?> GetDocumentHistoryAsync(
            int customerId,
            int documentTypeId,
            byte slotNo,
            IReadOnlyCollection<int>? locationIds);
    }
}
