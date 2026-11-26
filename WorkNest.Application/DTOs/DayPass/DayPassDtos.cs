using System;
using System.Collections.Generic;

namespace WorkNest.Application.DTOs.DayPass
{
    /// <summary>A customer entered at the desk (a new WN_Customers row).</summary>
    public class DayPassWalkIn
    {
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public string? Cnic { get; set; }
        public string? Email { get; set; }
        public string? Company { get; set; }
    }

    /// <summary>POST api/day-passes and POST api/day-passes/quote (docs/superpowers/specs/2026-11-26-day-pass-api-contract.md).</summary>
    public class DayPassRequest
    {
        public string? CustomerCode { get; set; }
        public DayPassWalkIn? WalkIn { get; set; }
        public int LocationId { get; set; }
        public int SpaceId { get; set; }
        /// <summary>yyyy-MM-dd</summary>
        public string? Date { get; set; }
        /// <summary>full | morning | evening</summary>
        public string? Window { get; set; }
        public bool WholeSpace { get; set; }
        public int Seats { get; set; }
        /// <summary>total | perSeat</summary>
        public string? PriceMode { get; set; }
        public decimal Amount { get; set; }
        public bool ApplyTax { get; set; }
        /// <summary>cnic | reception</summary>
        public string? AccessMode { get; set; }
        /// <summary>cash | bank</summary>
        public string? PaymentMethod { get; set; }
        /// <summary>AccountsCOA id from GET api/fee-receipts/accounts (isCashInHand = false).</summary>
        public int? BankAccountId { get; set; }
        public string? PaymentReference { get; set; }
        public string? Note { get; set; }
    }

    public class DayPassQuote
    {
        public decimal SubTotal { get; set; }
        public decimal TaxPercent { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }
        public int Seats { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class DayPassSpace
    {
        public int SpaceId { get; set; }
        public string SpaceName { get; set; } = "";
        /// <summary>PrivateOffice | SharedSpace | MeetingRoom</summary>
        public string SpaceType { get; set; } = "";
        public int LocationId { get; set; }
        public string LocationName { get; set; } = "";
        public int Capacity { get; set; }
        public int SeatsFree { get; set; }
        public bool WholeSpaceFree { get; set; }
        /// <summary>The space configuration's PricePerDay (per seat for a shared space, per room otherwise); null = none set.</summary>
        public decimal? ListPrice { get; set; }
    }

    public class DayPassHistoryEntry
    {
        public DateTime At { get; set; }
        public string ByName { get; set; } = "";
        public string Action { get; set; } = "";
        public string? Note { get; set; }
    }

    public class DayPassDto
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public string PassNumber { get; set; } = "";
        public string Status { get; set; } = "Active";
        public string Date { get; set; } = "";
        public string Window { get; set; } = "full";
        public string WindowLabel { get; set; } = "";
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public int LocationId { get; set; }
        public string LocationName { get; set; } = "";
        public int SpaceId { get; set; }
        public string SpaceName { get; set; } = "";
        public string SpaceType { get; set; } = "";
        public bool WholeSpace { get; set; }
        public int Seats { get; set; }
        public string CustomerCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }
        public string PriceMode { get; set; } = "total";
        public decimal UnitPrice { get; set; }
        public decimal SubTotal { get; set; }
        public decimal TaxPercent { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }
        public string PaymentMethod { get; set; } = "cash";
        public string? BankAccountName { get; set; }
        public string? PaymentReference { get; set; }
        public int? InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
        public int? ReceiptId { get; set; }
        public string? ReceiptNumber { get; set; }
        public string AccessMode { get; set; } = "reception";
        public string CreatedByName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string? CancelledByName { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? CancelReason { get; set; }
        public List<DayPassHistoryEntry>? History { get; set; }
    }

    public class DayPassTotals
    {
        public int Count { get; set; }
        public decimal SubTotal { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }
    }

    public class DayPassPage
    {
        public List<DayPassDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public DayPassTotals Totals { get; set; } = new();
    }

    public class DayPassCustomerRow
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Company { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Cnic { get; set; }
    }

    public class DayPassCancelRequest
    {
        public string? Reason { get; set; }
    }

    // ---- repository-facing ----------------------------------------------------------------------

    /// <summary>Everything the repository needs to save a pass (all figures already worked out and validated by the service).</summary>
    public class DayPassCreateData
    {
        public DayPassRequest Request { get; set; } = new();
        public DateTime Date { get; set; }
        public string Window { get; set; } = "full";
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        /// <summary>What the booking row blocks: the window, ending at midnight at the latest (the booking tables are date based).</summary>
        public DateTime BlockStart { get; set; }
        public DateTime BlockEnd { get; set; }
        public string Shift { get; set; } = "24_7";
        public bool WholeSpace { get; set; }
        public int Seats { get; set; }
        public string PriceMode { get; set; } = "total";
        public decimal UnitPrice { get; set; }
        public decimal SubTotal { get; set; }
        public decimal TaxPercent { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }
        public int ActorId { get; set; }
        public string? ActorEmail { get; set; }
        public string ActorName { get; set; } = "";
        public DateTime Today { get; set; }
        public DateTime Now { get; set; }
    }

    /// <summary>What the repository knows of a space.</summary>
    public class DayPassSpaceInfo
    {
        public int SpaceId { get; set; }
        public string Name { get; set; } = "";
        public int LocationId { get; set; }
        public string LocationName { get; set; } = "";
        public int Capacity { get; set; }
        public string Category { get; set; } = "";
        public bool IsActive { get; set; }
        public decimal TaxPercent { get; set; }
    }

    public class DayPassBookingInfo
    {
        public int DayPassId { get; set; }
        public string AccessMode { get; set; } = "reception";
        public int Seats { get; set; }
        public string Status { get; set; } = "Active";
    }

    /// <summary>A pass whose door access has to be taken away now (ended or cancelled).</summary>
    public class DayPassAccessDue
    {
        public int DayPassId { get; set; }
        public int BookingId { get; set; }
        public int? BookingDetailId { get; set; }
        public string PassNumber { get; set; } = "";
        public bool Cancelled { get; set; }
    }

    public class DayPassListFilter
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string Status { get; set; } = "all";
        public int? LocationId { get; set; }
        public string? Search { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    /// <summary>Thrown by the repository when a rule is broken after the checks (not available any more, unknown customer...). Status 409 / 400.</summary>
    public class DayPassException : Exception
    {
        public int StatusCode { get; }
        public DayPassException(int statusCode, string message) : base(message) { StatusCode = statusCode; }
    }
}
