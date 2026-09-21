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
        Task<AgreementResponseDto> MarkAgreementSignedAsync(int agreementId, int? userId, string? note);
    }
}
