using System;
using System.Collections.Generic;

namespace WorkNest.Application.DTOs.Quotation
{
    public class QuotationRequest
    {
        public int CustomerId { get; set; }
        public int SpaceId { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        // Discount: type is "Percentage" or "Amount"
        public string DiscountType { get; set; } = "Percentage";
        public decimal DiscountPercentage { get; set; } = 0;
        public decimal DiscountValue { get; set; } = 0;
        // Security deposit override (null = use calculated default)
        public decimal? SecurityDepositOverride { get; set; }
        // Floor (optional)
        public int? FloorId { get; set; }
        public string? Remarks { get; set; }
        public DateTime ValidUntil { get; set; }
    }

    public class QuotationResponse
    {
        public int Id { get; set; }
        public string? Guid { get; set; }
        public string? QuotationNumber { get; set; }
        public DateTime QuotationDate { get; set; }
        public DateTime ValidUntil { get; set; }
        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public int SpaceId { get; set; }
        public string? SpaceName { get; set; }
        public string? SpaceCode { get; set; }
        public string? LocationName { get; set; }
        public string? SpaceTypeName { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public decimal SubtotalAmount { get; set; }
        public string DiscountType { get; set; } = "Percentage";
        public decimal DiscountPercentage { get; set; }
        public decimal DiscountValue { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal SecurityDeposit { get; set; }
        public decimal TotalAmount { get; set; }
        public string? Remarks { get; set; }
        public string? Status { get; set; }
        public int Version { get; set; }
        public bool IsActive { get; set; }
        public List<QuotationDetailDto> Details { get; set; } = new List<QuotationDetailDto>();
    }

    public class QuotationDetailDto
    {
        public string FeeType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Amount { get; set; }
    }

    public class SendQuotationEmailRequest
    {
        public string? Email { get; set; }
        public string? PdfBase64 { get; set; }
        public string? QuotationLink { get; set; }
    }
}
