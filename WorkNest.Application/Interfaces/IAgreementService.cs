using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Agreement;

namespace WorkNest.Application.Interfaces
{
    public interface IAgreementService
    {
        Task<AgreementResponseDto> SendAgreementAsync(SendAgreementRequest request, int? userId);
        Task<(byte[] PdfBytes, AgreementResponseDto Agreement)> GenerateLeaseAgreementAsync(GenerateLeaseAgreementRequest request, int? userId);
        Task<byte[]> GetAgreementPdfAsync(int agreementId);
        Task<(IEnumerable<AgreementResponseDto> Rows, int Total)> GetAgreementsListAsync(int page, int limit, string? search, string? status);
        Task<AgreementResponseDto> MarkAgreementSignedAsync(int agreementId, int? userId, string? note, DateTime? signedDate = null);
        /// <summary>Customer portal: the logged-in customer's agreements (empty when the user has no customer record).</summary>
        Task<IEnumerable<IDictionary<string, object?>>> GetMyAgreementsAsync(string email);
        /// <summary>Customer portal: true when the agreement belongs to the logged-in customer.</summary>
        Task<bool> IsOwnAgreementAsync(string email, int agreementId);
        /// <summary>Customer uploaded the signed copy: hold it for admin verification with the date they signed.</summary>
        Task MarkCustomerSignedUploadAsync(int agreementId, DateTime signedDate);
        Task<AgreementResponseDto?> GetAgreementByIdAsync(int agreementId);
        Task UpdateSignedPdfInfoAsync(int agreementId, string path, DateTime uploadedAtUtc);
        Task<bool> DeleteAgreementAsync(int agreementId);
        Task<bool> DeleteSignedPdfAsync(int agreementId);
    }
}
