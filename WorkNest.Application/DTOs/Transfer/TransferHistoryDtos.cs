using System;
using System.Collections.Generic;

namespace WorkNest.Application.DTOs.Transfer
{
    /// <summary>One completed same-location space transfer (dbo.WN_BookingTransfers) with its request, when it went through one.</summary>
    public class TransferHistoryItemDto
    {
        public int Id { get; set; }
        public DateTime EffectiveDate { get; set; }
        public DateTime CreatedOn { get; set; }
        public int OldBookingId { get; set; }
        public int NewBookingId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerCode { get; set; }
        public int LocationId { get; set; }
        public string? LocationName { get; set; }
        public string? OldSpaceName { get; set; }
        public string? NewSpaceName { get; set; }
        public decimal OldMonthlyRate { get; set; }
        public decimal NewMonthlyRate { get; set; }
        public decimal Adjustment { get; set; }
        /// <summary>Billed, CreditStored, ... as the procedure wrote it.</summary>
        public string? SettlementOutcome { get; set; }
        public int? SettlementInvoiceId { get; set; }
        public string? SettlementInvoiceNumber { get; set; }
        public decimal? SettlementInvoiceAmount { get; set; }
        public decimal TransferFee { get; set; }
        public string? Notes { get; set; }
        public string? InitiatedByName { get; set; }
        public string? InitiatedByEmail { get; set; }
        /// <summary>Request details; all null for a transfer an admin ran directly.</summary>
        public int? RequestId { get; set; }
        public string? RequestedByName { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTime? DecidedAt { get; set; }
        public decimal? OldPerSeatPrice { get; set; }
        public decimal? NewPerSeatPrice { get; set; }
        public decimal? OldDeposit { get; set; }
        public decimal? NewDeposit { get; set; }
        public string? PrepaidMode { get; set; }
        /// <summary>Transfer credit still on the new booking.</summary>
        public decimal? CreditBalance { get; set; }
        /// <summary>True when no request is attached (an admin or super admin transferred directly).</summary>
        public bool IsDirect => RequestId == null;
    }

    public class TransferHistoryPageDto
    {
        public List<TransferHistoryItemDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
