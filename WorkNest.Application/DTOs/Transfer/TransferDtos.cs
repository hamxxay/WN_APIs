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
        /// <summary>The booking's offer (script OFFER_01_offers); null without one. The CMS warns before a transfer: the offer does not carry over.</summary>
        public int? OfferId { get; set; }
        public string? OfferName { get; set; }
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

    // Phase 2 DTOs: Branch Transfers & Pending Payments

    public class BranchTransferCreateRequestDto
    {
        public int BookingId { get; set; }
        public int ToLocationId { get; set; }
        public int RequestedCapacity { get; set; } = 1;
        public DateTime? EffectiveDate { get; set; }
        public decimal TransferFee { get; set; } = 0.00m;
        public string? Notes { get; set; }
    }

    public class BranchTransferCancelRequestDto
    {
        public string? Reason { get; set; }
    }

    public class BranchTransferDeclineRequestDto
    {
        public string? Reason { get; set; }
    }

    public class BranchTransferAcceptWithSpaceRequestDto
    {
        public int SpaceId { get; set; }
    }

    public class BranchTransferResolveMatchRequestDto
    {
        public string ResolutionType { get; set; } = "existing"; // "existing" or "new"
        public int? SelectedCustomerId { get; set; }
        public string? Note { get; set; }
    }

    public class PendingPaymentConfirmRequestDto
    {
        public string BankAccount { get; set; } = string.Empty;
        public string PaymentReference { get; set; } = string.Empty;
        public DateTime? PaymentDate { get; set; }
    }

    public class TransferRequestSendingSummaryDto
    {
        public int Id { get; set; }
        public int OldBookingId { get; set; }
        public int FromLocationId { get; set; }
        public string FromLocationName { get; set; } = string.Empty;
        public int FromCompanyId { get; set; }
        public int ToLocationId { get; set; }
        public string ToLocationName { get; set; } = string.Empty;
        public int ToCompanyId { get; set; }
        public int RequestedCapacity { get; set; }
        public DateTime EffectiveDate { get; set; }
        public string? Notes { get; set; }
        public decimal TransferFee { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? DeclineCancelReason { get; set; }
        public int CreatedById { get; set; }
        public string CreatedByEmail { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int? TargetSpaceId { get; set; }
        public string? TargetSpaceName { get; set; }
        public string? AcceptedByEmail { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public int? MatchedCustomerId { get; set; }
        public int? NewBookingId { get; set; }
        public DateTime? FinalizedAt { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
        public string? OldSpaceName { get; set; }
    }

    public class TransferRequestIncomingSummaryDto
    {
        // Strictly capacity-only view for receiving branch
        public int Id { get; set; }
        public int FromLocationId { get; set; }
        public string FromLocationName { get; set; } = string.Empty;
        public int ToLocationId { get; set; }
        public string ToLocationName { get; set; } = string.Empty;
        public int RequestedCapacity { get; set; }
        public DateTime EffectiveDate { get; set; }
        public string? Notes { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int? TargetSpaceId { get; set; }
        public string? TargetSpaceName { get; set; }
        public string? MatchResolutionNote { get; set; }
        public int? NewBookingId { get; set; }
    }

    public class MatchCandidateCustomerDto
    {
        public int CustomerVendorId { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? CNIC { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public int CompanyId { get; set; }
        public string MatchReason { get; set; } = string.Empty;
    }

    public class MatchResolutionDetailsDto
    {
        public int RequestId { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime EffectiveDate { get; set; }
        public int RequestedCapacity { get; set; }
        public int? TargetSpaceId { get; set; }
        public string? TargetSpaceName { get; set; }
        public string FromLocationName { get; set; } = string.Empty;
        public string ToLocationName { get; set; } = string.Empty;
        public string? OldCustomerCode { get; set; }
        public string? OldCustomerName { get; set; }
        public string? OldCNIC { get; set; }
        public string? OldPhone { get; set; }
        public string? OldEmail { get; set; }
        public string? OldCustomerCompany { get; set; }
        public List<MatchCandidateCustomerDto> Candidates { get; set; } = new();
    }

    public class PendingPaymentSummaryDto
    {
        public int Id { get; set; }
        public int TransferRequestId { get; set; }
        public int FromCompanyId { get; set; }
        public int FromLocationId { get; set; }
        public string FromBranchName { get; set; } = string.Empty;
        public int ToCompanyId { get; set; }
        public int ToLocationId { get; set; }
        public string ToBranchName { get; set; } = string.Empty;
        public int OldBookingId { get; set; }
        public int NewBookingId { get; set; }
        public decimal RentAmount { get; set; }
        public decimal DepositAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public int? ReceiptId { get; set; }
        public bool IsConfirmed { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public string? ConfirmedByEmail { get; set; }
        public string? BankAccount { get; set; }
        public string? PaymentReference { get; set; }
        public DateTime? PaymentDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public int AgeInDays { get; set; }
    }
}
