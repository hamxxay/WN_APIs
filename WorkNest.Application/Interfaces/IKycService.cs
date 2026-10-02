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
        Task<(IEnumerable<KycCustomerListItemViewModel> Items, int Total)> GetCustomerKycListAsync(int page, int limit, string? search, int? userLocationId, bool isSuperAdmin);
        
        Task<CustomerKycPageViewModel?> GetCustomerKycPortalAsync(string customerIdOrGuid, int? userLocationId, bool isSuperAdmin, bool canVerifyAndReject);
        
        Task<(bool Success, string Message, int? DocumentId)> UploadOrReplaceDocumentAsync(
            int customerId,
            int documentTypeId,
            byte slotNo,
            string? holderName,
            DateTime? expiryDate,
            IFormFile file,
            int uploadedByUserId,
            int? userLocationId,
            bool isSuperAdmin);

        Task<(bool Success, string Message)> VerifyOrRejectDocumentAsync(
            int documentId,
            byte status,
            string? remarks,
            int verifiedByUserId,
            string userRole,
            int? userLocationId,
            bool isSuperAdmin);

        Task<(Stream? Stream, string ContentType, string FileName, string Error)> DownloadDocumentAsync(
            int documentId,
            int? userLocationId,
            bool isSuperAdmin);

        Task<IEnumerable<CustomerKYCDocument>> GetDocumentHistoryAsync(
            int customerId,
            int documentTypeId,
            byte slotNo,
            int? userLocationId,
            bool isSuperAdmin);
    }
}
