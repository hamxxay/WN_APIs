using System;
using System.Collections.Generic;

namespace WorkNest.Application.DTOs.Transfer
{
    public class TransferPreviewRequest
    {
        public int BookingId { get; set; }
        public int NewSpaceId { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public decimal TransferFee { get; set; } = 0m;
    }

    public class TransferExecuteRequest
    {
        public int BookingId { get; set; }
        public int NewSpaceId { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public decimal TransferFee { get; set; } = 0m;
        public string Note { get; set; } = string.Empty;
    }

    public class BlockingInvoiceDto
    {
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime? DueOn { get; set; }
        public Guid? PublicId { get; set; }
    }

    public class ActiveBookingSummaryDto
    {
        public int BookingId { get; set; }
        public Guid BookingPublicId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerCode { get; set; }
        public int SpaceId { get; set; }
        public string? SpaceName { get; set; }
        public string? SpaceCode { get; set; }
        public int LocationId { get; set; }
        public string? LocationName { get; set; }
        public DateTime StartOn { get; set; }
        public DateTime EndOn { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal DiscountPercentage { get; set; }
        public string? OfferingType { get; set; }
        public decimal SecurityDeposit { get; set; }
    }

    public class TargetSpaceSummaryDto
    {
        public int SpaceId { get; set; }
        public string? SpaceName { get; set; }
        public string? SpaceCode { get; set; }
        public int LocationId { get; set; }
        public string? LocationName { get; set; }
        public int Capacity { get; set; }
        public decimal MonthlyRate { get; set; }
        public decimal MaxDiscountPercent { get; set; }
        public string? OfferingType { get; set; }
        public decimal OfferingTypeDiscountCap { get; set; }
    }

    public class DiscountValidationDto
    {
        public decimal DiscountPercentage { get; set; }
        public decimal OfferingTypeDiscountCap { get; set; }
        public bool IsExceeded { get; set; }
        public string? Message { get; set; }
    }

    public class TransferSettlementResult
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public DateTime EffectiveDate { get; set; }
        public decimal OldMonthlyRate { get; set; }
        public decimal NewMonthlyRate { get; set; }
        public int DaysUsed { get; set; }
        public int RemainingDays { get; set; }
        public decimal CreditAtA { get; set; }
        public decimal RepricedAtB { get; set; }
        public decimal Adjustment { get; set; }
        public decimal TransferFee { get; set; }
        public decimal TotalDue { get; set; }
        public string Outcome { get; set; } = string.Empty; // "Billed", "CreditStored", "NoAdjustment"
        public string OutcomeDescription { get; set; } = string.Empty;
    }

    public class TransferPreviewResponseDto
    {
        public bool IsValid { get; set; }
        public List<string> ValidationErrors { get; set; } = new();
        public ActiveBookingSummaryDto? CurrentBooking { get; set; }
        public TargetSpaceSummaryDto? TargetSpace { get; set; }
        public DiscountValidationDto? DiscountValidation { get; set; }
        public List<BlockingInvoiceDto> BlockingInvoices { get; set; } = new();
        public TransferSettlementResult? Settlement { get; set; }
    }

    public class TransferExecuteResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int OldBookingId { get; set; }
        public int NewBookingId { get; set; }
        public int? TransferId { get; set; }
        public int? SettlementInvoiceId { get; set; }
        public string? SettlementInvoiceNumber { get; set; }
        public decimal CreditBalance { get; set; }
        public bool AddendumPdfGenerated { get; set; }
        public bool AddendumEmailSent { get; set; }
    }
}

