using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WorkNest.Application.DTOs.Quotation
{
    /// <summary>What a user may set on one month of the rent schedule (API contract: ScheduleInputRow).</summary>
    public class ScheduleInputRow
    {
        public int RowNo { get; set; }
        /// <summary>"Percentage" or "Amount".</summary>
        public string DiscountType { get; set; } = "Percentage";
        public decimal DiscountValue { get; set; }
        /// <summary>Null = no deposit in this month.</summary>
        public decimal? SecurityDeposit { get; set; }
    }

    /// <summary>One month of the rent schedule, everything worked out (API contract: ScheduleRow).</summary>
    public class ScheduleRowDto : ScheduleInputRow
    {
        public string Label { get; set; } = string.Empty;
        public DateOnly PeriodStart { get; set; }
        public DateOnly PeriodEnd { get; set; }
        public decimal MonthFraction { get; set; }
        public bool IsShort { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal RoomRent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal RentAfterDiscount { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }
        // Bookings only (part 2); never set on a quotation.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? InvoiceId { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? InvoiceNumber { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Locked { get; set; }
    }

    public class ScheduleTotalsDto
    {
        public decimal MonthlyRent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal RentAfterDiscount { get; set; }
        public decimal SecurityDeposit { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>The rent schedule of a quotation (API contract: Schedule).</summary>
    public class RentScheduleDto
    {
        public List<ScheduleRowDto> Rows { get; set; } = new();
        public ScheduleTotalsDto Totals { get; set; } = new();
        public decimal TaxPercent { get; set; }
        public decimal SupportRate { get; set; }
        public int Seats { get; set; }
        public decimal PerSeatPrice { get; set; }
        public decimal DefaultSecurityDeposit { get; set; }
        /// <summary>The discount cap of the whole contract: the offering type's cap % (widened for a raised base price) of the table's
        /// total rent. The table's total discount (total rent - total rent after discount) may not be more. Null = not checked (no cap).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? MaxDiscountPercent { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? MaxDiscountAmount { get; set; }
        /// <summary>True when the table's total discount is above <see cref="MaxDiscountAmount"/> (the quotation cannot be saved).</summary>
        public bool OverDiscountCap { get; set; }
    }

    /// <summary>POST api/quotation/schedule-preview: the staff form's fields plus optional user rows.</summary>
    public class SchedulePreviewRequest : QuotationRequest
    {
        public List<ScheduleInputRow>? Rows { get; set; }
    }
}
