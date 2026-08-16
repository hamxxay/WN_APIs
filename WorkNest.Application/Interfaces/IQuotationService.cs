using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Quotation;

namespace WorkNest.Application.Interfaces
{
    public interface IQuotationService
    {
        Task<QuotationResponse> CreateQuotationAsync(QuotationRequest request, int? createdById);
        Task<QuotationResponse?> GetQuotationByIdAsync(int id);
        Task<(IEnumerable<QuotationResponse> Rows, int Total)> GetQuotationsAsync(int page, int limit, string? search);
        Task<IEnumerable<QuotationResponse>> GetQuotationHistoryAsync(int customerId, int spaceId);
        Task<IDictionary<string, object?>> ConvertQuotationToBookingAsync(int quotationId, int? createdById);
    }
}
