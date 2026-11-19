using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkNest.Application.DTOs.RentSchedule
{
    /// <summary>Writes / reads a DateTime as "yyyy-MM-dd" (the schedule's period dates carry no time).</summary>
    public sealed class ScheduleDateJsonConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal).Date;

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    /// <summary>What a client sends per month (spec section 4: ScheduleInputRow).</summary>
    public class BookingScheduleInputRow
    {
        public int RowNo { get; set; }
        /// <summary>"Percentage" or "Amount".</summary>
        public string? DiscountType { get; set; }
        public decimal DiscountValue { get; set; }
        /// <summary>Null = no security deposit in this month.</summary>
        public decimal? SecurityDeposit { get; set; }
    }

    /// <summary>One month of a booking's rent schedule (spec section 4: ScheduleRow).</summary>
    public class BookingScheduleRow : BookingScheduleInputRow
    {
        public string Label { get; set; } = "";
        [JsonConverter(typeof(ScheduleDateJsonConverter))] public DateTime PeriodStart { get; set; }
        [JsonConverter(typeof(ScheduleDateJsonConverter))] public DateTime PeriodEnd { get; set; }
        public decimal MonthFraction { get; set; } = 1m;
        public bool IsShort { get; set; }
        public decimal MonthlyRent { get; set; }
        public decimal ServiceCharge { get; set; }
        public decimal RoomRent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal RentAfterDiscount { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }
        public int? InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public bool Locked { get; set; }
    }

    public class BookingScheduleTotals
    {
        public decimal MonthlyRent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal RentAfterDiscount { get; set; }
        public decimal SecurityDeposit { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }
    }

    public class BookingScheduleView
    {
        public List<BookingScheduleRow> Rows { get; set; } = new();
        public BookingScheduleTotals Totals { get; set; } = new();
        public decimal TaxPercent { get; set; }
        public decimal SupportRate { get; set; }
        public int Seats { get; set; }
        public decimal PerSeatPrice { get; set; }
        public decimal DefaultSecurityDeposit { get; set; }
    }

    public class BookingSchedulePendingChange
    {
        public int Id { get; set; }
        public string? RequestedBy { get; set; }
        public DateTime RequestedAt { get; set; }
    }

    /// <summary>GET api/booking/{id}/schedule.</summary>
    public class BookingScheduleResponse
    {
        public bool HasSchedule { get; set; }
        public BookingSchedulePendingChange? PendingChange { get; set; }
        public BookingScheduleView Schedule { get; set; } = new();
    }

    public class SaveBookingScheduleRequest
    {
        public List<BookingScheduleInputRow>? Rows { get; set; }
    }

    /// <summary>PUT answer: saved straight away, or waiting for the sales manager.</summary>
    public class SaveBookingScheduleResult
    {
        public bool PendingApproval { get; set; }
        public int? ChangeId { get; set; }
        public BookingScheduleResponse? Schedule { get; set; }
    }

    public class ApproveBookingScheduleRequest
    {
        public List<BookingScheduleInputRow>? Rows { get; set; }
        public string? Note { get; set; }
    }

    public class SendBackBookingScheduleRequest
    {
        public string? Note { get; set; }
    }

    /// <summary>A row of the manager's queue (api/booking-schedule-approvals).</summary>
    public class BookingScheduleChangeRowDto
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public string? CustomerName { get; set; }
        public string? SpaceName { get; set; }
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        public int RequestedById { get; set; }
        public string? RequestedBy { get; set; }
        public DateTime RequestedAt { get; set; }
        /// <summary>Pending, Approved or SentBack.</summary>
        public string Status { get; set; } = "Pending";
        public string? DecidedBy { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? Note { get; set; }
    }

    public class BookingScheduleChangeDetailDto : BookingScheduleChangeRowDto
    {
        /// <summary>The booking's table now.</summary>
        public BookingScheduleView? Current { get; set; }
        /// <summary>The table with the requested rows applied (null when it no longer applies; see <see cref="Problem"/>).</summary>
        public BookingScheduleView? Requested { get; set; }
        public string? Problem { get; set; }
    }
}
