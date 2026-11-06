using System;
using System.Collections.Generic;

namespace WorkNest.Application.DTOs.NoticePeriod
{
    public class SubmitNoticeRequest
    {
        public int BookingId { get; set; }
        public string? Note { get; set; }
    }

    public class StaffIssueNoticeRequest
    {
        public int? NoticeId { get; set; }
        public int? BookingId { get; set; }
        public DateTime NoticeDate { get; set; }
        public int? NoticeDays { get; set; }
        public string? StaffNote { get; set; }
    }

    public class StaffUpdateNoticeDateRequest
    {
        public DateTime NoticeDate { get; set; }
        public string? StaffNote { get; set; }
    }

    public class StaffDeclineNoticeRequest
    {
        public string DeclineReason { get; set; } = string.Empty;
    }

    public class NoticePeriodItemDto
    {
        public int Id { get; set; }
        public Guid PublicId { get; set; }
        public int BookingId { get; set; }
        public int CustomerId { get; set; }
        public int UserId { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }
        public string Source { get; set; } = "Mobile";
        public DateTime RequestedOn { get; set; }
        public DateTime? NoticeDate { get; set; }
        public int NoticeDays { get; set; }
        public DateTime? ExitDate { get; set; }
        public string Status { get; set; } = "Requested";
        public string? StaffNote { get; set; }
        public string? DeclineReason { get; set; }
        public int? IssuedBy { get; set; }
        public string? IssuedByName { get; set; }
        public DateTime? IssuedOn { get; set; }
        public DateTime? WithdrawnOn { get; set; }
        public DateTime CreatedOn { get; set; }
        public int? SpaceId { get; set; }
        public string? SpaceName { get; set; }
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        public DateTime? BookingStartOn { get; set; }
        public DateTime? BookingEndOn { get; set; }
        public decimal? MonthlyRent { get; set; }
        public decimal SecurityDepositHeld { get; set; }
        public int? SpaceTypeId { get; set; }
        public string? SpaceTypeName { get; set; }
        public int? SettlementId { get; set; }
        public DateTime? SettledOn { get; set; }
    }

    public class SettlementPreCheckDto
    {
        public int NoticeId { get; set; }
        public int BookingId { get; set; }
        public int CustomerId { get; set; }
        public int UserId { get; set; }
        public string? CustomerCode { get; set; }
        public DateTime? NoticeDate { get; set; }
        public int NoticeDays { get; set; }
        public DateTime? ExitDate { get; set; }
        public string NoticeStatus { get; set; } = string.Empty;
        public DateTime CurrentDate { get; set; }
        public bool AllowedToSettle { get; set; }
        public int? SpaceId { get; set; }
        public string? SpaceName { get; set; }
        public int? SpaceTypeId { get; set; }
        public string? SpaceTypeName { get; set; }
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal DepositHeld { get; set; }

        public List<UnpaidInvoiceItemDto> UnpaidInvoices { get; set; } = new();
        public List<AdvancePaidRentItemDto> AdvancePaidRents { get; set; } = new();
        public List<PayoutAccountItemDto> PayoutAccounts { get; set; } = new();
    }

    public class UnpaidInvoiceItemDto
    {
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime? IssuedOn { get; set; }
        public DateTime? DueOn { get; set; }
        public DateTime? BillingPeriodStart { get; set; }
        public DateTime? BillingPeriodEnd { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal PaidTotal { get; set; }
        public decimal BalanceDue { get; set; }
        public int StatusId { get; set; }
        public string? StatusDescription { get; set; }
    }

    public class AdvancePaidRentItemDto
    {
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime? BillingPeriodStart { get; set; }
        public DateTime? BillingPeriodEnd { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal PaidTotal { get; set; }
        public int DaysAfterExitDate { get; set; }
        public decimal UnearnedAdvanceAmount { get; set; }
    }

    public class PayoutAccountItemDto
    {
        public int Id { get; set; }
        public string? AccountCode { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public string? AccountNature { get; set; }
        public bool IsCashInHand { get; set; }
    }

    public class SaveNoticeSettlementRequest
    {
        public int NoticeId { get; set; }
        public decimal DepositHeld { get; set; }
        public int PayoutAccountId { get; set; }
        public DateTime SettledOn { get; set; }
        public List<NoticeSettlementLineDto> Lines { get; set; } = new();
    }

    public class NoticeSettlementLineDto
    {
        public string LineType { get; set; } = "Restoration"; // Restoration, Damages, Other
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; } = 1; // e.g. SqFt
        public decimal Rate { get; set; } = 0; // Rate per SqFt or unit
        public decimal Amount { get; set; }
        public int SortOrder { get; set; } = 1;
    }

    public class NoticeSettlementResultDto
    {
        public int SettlementId { get; set; }
        public int NoticeId { get; set; }
        public int BookingId { get; set; }
        public decimal DepositHeld { get; set; }
        public decimal TotalDeduction { get; set; }
        public decimal RefundAmount { get; set; }
        public decimal ShortfallAmount { get; set; }
        public int? VoucherId { get; set; }
        public int? ShortfallInvoiceId { get; set; }
        public string? ShortfallInvoiceNumber { get; set; }
        public DateTime SettledOn { get; set; }
    }
}

