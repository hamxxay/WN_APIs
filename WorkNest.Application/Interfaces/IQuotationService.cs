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
        Task<(IEnumerable<QuotationResponse> Rows, int Total)> GetQuotationsAsync(int page, int limit, string? search, int? locationId = null);
        Task<IEnumerable<QuotationResponse>> GetQuotationHistoryAsync(int customerId, int spaceId);
        Task<IEnumerable<QuotationResponse>> GetQuotationsByCustomerAsync(int customerId);
        Task<IDictionary<string, object?>> ConvertQuotationToBookingAsync(int quotationId, int? createdById);
        Task SendQuotationEmailAsync(int quotationId, string? overrideEmail);
        Task<QuotationResponse> AcceptQuotationAsync(int quotationId, int version, int customerId, string? note, int? userId);
        Task<QuotationResponse> DeclineQuotationAsync(int quotationId, int version, int customerId, string note, int? userId);
        Task<QuotationResponse> CreateNewVersionAsync(int quotationId, int? createdById);
        Task<IEnumerable<QuotationResponse>> GetVersionsAsync(int quotationId);
        Task<IEnumerable<QuotationActivityDto>> GetActivitiesAsync(int? quotationId, int limit);
        Task SendQuotationAsync(int quotationId, int? userId);
    }
}
