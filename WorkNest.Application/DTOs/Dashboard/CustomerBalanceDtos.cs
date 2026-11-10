namespace WorkNest.Application.DTOs.Dashboard
{
    public class CustomerBalanceSummaryDto
    {
        public int TotalCustomersWithBalance { get; set; }
        public decimal TotalBalanceRemaining { get; set; }
        public decimal TotalOverdueBalance { get; set; }
        public int TotalOpenInvoices { get; set; }
        public int TotalCustomers { get; set; }
    }

    public class CustomerBalanceItemDto
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public int TotalInvoices { get; set; }
        public int OpenInvoicesCount { get; set; }
        public int OverdueInvoicesCount { get; set; }
        public decimal TotalInvoiced { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal BalanceRemaining { get; set; }
        public decimal OverdueBalance { get; set; }
        public string? LatestInvoiceDate { get; set; }
    }

    public class CustomerBalancesResponseDto
    {
        public CustomerBalanceSummaryDto Summary { get; set; } = new();
        public List<CustomerBalanceItemDto> Customers { get; set; } = new();
    }
}

