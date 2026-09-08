using System;
using System.Collections.Generic;
using WorkNest.Application.DTOs.Booking;

namespace WorkNest.Application.DTOs.Quotation
{
    public class QuotationRequest
    {
        public int CustomerId { get; set; }
        public int SpaceId { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public decimal? PerSeatBasePrice { get; set; }
        public int? Capacity { get; set; }
        public decimal? MonthlyBasePrice { get; set; }
        public decimal? MaxDiscountPercent { get; set; }
        public string DiscountType { get; set; } = "Percentage";
        public decimal DiscountPercentage { get; set; } = 0;
        public decimal DiscountValue { get; set; } = 0;
        public decimal? SecurityDepositOverride { get; set; }
        public int? BillingPeriodMonths { get; set; }
        public int? SecurityDepositMonths { get; set; }
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
        public string? CustomerCompany { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerAddress { get; set; }
        public int SpaceId { get; set; }
        public string? SpaceName { get; set; }
        public string? SpaceCode { get; set; }
        public string? LocationName { get; set; }
        public string? LocationAddress { get; set; }
        public string? CityName { get; set; }
        public string? SpaceTypeName { get; set; }

        public string SpaceType { get; set; } = string.Empty;
        public string BillingType { get; set; } = string.Empty;
        public List<ChallanFieldDto> Fields { get; set; } = new();

        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public decimal? PerSeatBasePrice { get; set; }
        public int? Capacity { get; set; }
        public decimal? MonthlyBasePrice { get; set; }
        public decimal? MaxDiscountPercent { get; set; }
        public decimal SubtotalAmount { get; set; }
        public string DiscountType { get; set; } = "Percentage";
        public decimal DiscountPercentage { get; set; }
        public decimal DiscountValue { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal SecurityDeposit { get; set; }
        public decimal TotalAmount { get; set; }
        public byte? SupportChargesId { get; set; }
        public decimal AppliedChargePercentage { get; set; } = 10.00m;
        public decimal AppliedTaxPercentage { get; set; } = 16.00m;
        public decimal SupportChargeAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TaxAmountOnAdvanceRent { get; set; }
        public decimal TaxAmountOnContract { get; set; }
        public decimal TotalPayable { get; set; }
        public string? Remarks { get; set; }
        public string? Status { get; set; }
        public int Version { get; set; }
        public bool IsActive { get; set; }

        public int BillingPeriodMonths { get; set; } = 3;
        public int SecurityDepositMonths { get; set; } = 1;
        public string? BillingPeriod { get; set; }
        public string? BillingPeriodLabel { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal CurrentCycleAmount { get; set; }
        public decimal TotalContractAmount { get; set; }
        public ContractDetailsDto? Contract { get; set; }

        public bool CanRespond { get; set; }
        public string? CustomerNote { get; set; }
        public DateTime? ResponseDate { get; set; }
        public List<QuotationActivityDto> Activities { get; set; } = new List<QuotationActivityDto>();

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

    public class AcceptQuotationRequest
    {
        public string? Note { get; set; }
    }

    public class DeclineQuotationRequest
    {
        public string Note { get; set; } = string.Empty;
    }

    public class QuotationActivityDto
    {
        public int Id { get; set; }
        public int QuotationId { get; set; }
        public int Version { get; set; }
        public string ActivityType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? CustomerNote { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}