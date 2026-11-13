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
        /// <summary>A customer's quotations; limit null / &lt; 1 = all. sentToCustomerOnly: only those sent to the customer. Total = before paging.</summary>
        Task<(IEnumerable<QuotationResponse> Items, int Total)> GetQuotationsByCustomerPageAsync(int customerId, int? page, int? limit, bool sentToCustomerOnly = false);
        Task<IDictionary<string, object?>> ConvertQuotationToBookingAsync(int quotationId, int? createdById);
        Task SendQuotationEmailAsync(int quotationId, string? overrideEmail);
        Task<QuotationResponse> AcceptQuotationAsync(int quotationId, int version, int customerId, string? note, int? userId);
        Task<QuotationResponse> DeclineQuotationAsync(int quotationId, int version, int customerId, string note, int? userId);
        /// <summary>request null = a copy of the source version; with the staff form = the new version is built from the form.</summary>
        Task<QuotationResponse> CreateNewVersionAsync(int quotationId, int? createdById, QuotationRequest? request = null);
        Task<IEnumerable<QuotationResponse>> GetVersionsAsync(int quotationId);
        /// <param name="locationIds">Only quotations of these locations (location-bound staff); null = all.</param>
        Task<IEnumerable<QuotationActivityDto>> GetActivitiesAsync(int? quotationId, int limit, IReadOnlyCollection<int>? locationIds = null);
        /// <param name="locationIds">Only quotations of these locations (location-bound staff); null = all.</param>
        Task<(IEnumerable<QuotationResponseItemDto> Rows, int Total)> GetQuotationResponsesAsync(int page, int limit, string? search, IReadOnlyCollection<int>? locationIds = null);
        Task SendQuotationAsync(int quotationId, int? userId);
        /// <summary>The highest manual discount percentage the offering type allows for this quotation (same cap logic as create); null = no cap row.</summary>
        Task<decimal?> GetDiscountCapPercentAsync(int quotationId);
        /// <summary>
        /// The sales manager's edit before approving: type + value within the cap, amounts and deposit recomputed, offer snapshot kept.
        /// Returns the discount before / after as history labels. <see cref="WorkNest.Application.Interfaces.QuotationApprovalRuleException"/> (400) when over the cap.
        /// </summary>
        Task<(string From, string To, bool Changed)> ApplyDiscountEditAsync(int quotationId, string discountType, decimal discountValue, int? byUserId);
        Task<IEnumerable<OfferingTypeDto>> GetOfferingTypesAsync(bool? activeOnly = null, int? locationId = null);
    }
}
