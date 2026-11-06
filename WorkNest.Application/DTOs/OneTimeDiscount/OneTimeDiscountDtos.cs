using System;
using System.Collections.Generic;

namespace WorkNest.Application.DTOs.OneTimeDiscount
{
    public class OneTimeDiscountCreateRequest
    {
        public int BookingId { get; set; }
        /// <summary>"Invoice" or "Month"</summary>
        public string AppliesTo { get; set; } = "Invoice";
        /// <summary>Null = next upcoming invoice, or the 1-based upcoming invoice sequence / Invoice.Id</summary>
        public int? TargetInvoiceId { get; set; }
        /// <summary>First day of target calendar month (YYYY-MM-01) when AppliesTo == "Month"</summary>
        public DateTime? TargetMonth { get; set; }
        /// <summary>"Percentage" or "Fixed"</summary>
        public string DiscountType { get; set; } = "Percentage";
        public decimal DiscountValue { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class OneTimeDiscountPreviewRequest
    {
        public int BookingId { get; set; }
        public string AppliesTo { get; set; } = "Invoice";
        public int? TargetInvoiceId { get; set; }
        public DateTime? TargetMonth { get; set; }
        public string DiscountType { get; set; } = "Percentage";
        public decimal DiscountValue { get; set; }
        public DateTime? BillingPeriodStart { get; set; }
        public DateTime? BillingPeriodEnd { get; set; }
    }

    public class OneTimeDiscountDto
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public string AppliesTo { get; set; } = string.Empty;
        public int? TargetInvoiceId { get; set; }
        public DateTime? TargetMonth { get; set; }
        public string DiscountType { get; set; } = string.Empty;
        public decimal DiscountValue { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public int? AppliedInvoiceId { get; set; }
        public string? AppliedInvoiceNumber { get; set; }
        public DateTime? AppliedAt { get; set; }
        public decimal? AppliedDiscountAmount { get; set; }
        public int CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public string? CreatedByEmail { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? CancelledBy { get; set; }
        public string? CancelledByName { get; set; }
        public DateTime? CancelledAt { get; set; }
    }

    public class OneTimeDiscountPreviewDto
    {
        public int BookingId { get; set; }
        public string AppliesTo { get; set; } = "Invoice";
        public int? TargetInvoiceId { get; set; }
        public DateTime? TargetMonth { get; set; }
        public string DiscountType { get; set; } = "Percentage";
        public decimal DiscountValue { get; set; }
        public decimal MonthlyRent { get; set; }
        public DateTime? BillingPeriodStart { get; set; }
        public DateTime? BillingPeriodEnd { get; set; }
        public int BillingPeriodMonths { get; set; }
        public decimal EffectiveMonths { get; set; }
        public decimal MonthProrationShare { get; set; }
        public int ProratedDays { get; set; }
        public decimal InvoiceGrossRent { get; set; }
        public decimal TargetBaseRent { get; set; }
        public decimal ExistingBookingDiscountAmount { get; set; }
        public decimal ExistingBookingDiscountPct { get; set; }
        public decimal OneTimeDiscountAmount { get; set; }
        public decimal OneTimeEquivalentPct { get; set; }
        public decimal CombinedDiscountPct { get; set; }
        public decimal OfferingCapPct { get; set; }
        public bool IsCapExceeded { get; set; }
        public decimal RoomRentExclTaxBefore { get; set; }
        public decimal RoomRentExclTaxAfter { get; set; }
        public decimal ServiceCharges { get; set; }
        public decimal TaxOnServiceCharges { get; set; }
        public decimal SecurityDepositAmount { get; set; }
        public decimal TotalDiscountAfter { get; set; }
        public decimal GrandTotalBefore { get; set; }
        public decimal GrandTotalAfter { get; set; }
    }

    public class UpcomingInvoiceOptionDto
    {
        public int? TargetInvoiceId { get; set; }
        public string Label { get; set; } = string.Empty;
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public bool IsInitialInvoice { get; set; }
    }

    public class ContractMonthOptionDto
    {
        public string TargetMonth { get; set; } = string.Empty; // YYYY-MM-01
        public string Label { get; set; } = string.Empty;
        public bool IsProrated { get; set; }
        public int ProratedDays { get; set; }
        public bool IsAlreadyInvoiced { get; set; }
    }

    public class BookingOneTimeDiscountsSummaryDto
    {
        public int BookingId { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal ExistingBookingDiscountPct { get; set; }
        public decimal ExistingBookingDiscountAmount { get; set; }
        public decimal OfferingCapPct { get; set; }
        public decimal RemainingCapPct { get; set; }
        public List<OneTimeDiscountDto> Discounts { get; set; } = new();
        public List<UpcomingInvoiceOptionDto> UpcomingInvoices { get; set; } = new();
        public List<ContractMonthOptionDto> ContractMonths { get; set; } = new();
    }
}
