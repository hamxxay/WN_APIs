namespace WorkNest.Application.DTOs.Reports
{
    public class SecurityDepositReportFilterDto
    {
        public int? CustomerId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class SecurityDepositSummaryDto
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string? Company { get; set; }
        public int DepositCount { get; set; }
        public decimal TotalReceived { get; set; }
        public decimal TotalReleased { get; set; }
        public decimal TotalForfeited { get; set; }
        public decimal BalanceHeld { get; set; }
        public DateTime? LastHeldOn { get; set; }
    }

    public class SecurityDepositDetailDto
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public string? RefNo { get; set; }
        public decimal Amount { get; set; }
        public string State { get; set; } = string.Empty;
        public DateTime HeldOn { get; set; }
        public DateTime? ReleasedOn { get; set; }
        public DateTime? ForfeitedOn { get; set; }
        public string? ForfeitReason { get; set; }
        public string? Notes { get; set; }
    }

    public class CustomerReportDropdownDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
    }
}
