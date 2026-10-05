using WorkNest.Application.DTOs.OrderStatus;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Status IDs from dbo.OrderStatus, looked up by description and cached.</summary>
    public interface IOrderStatusService
    {
        /// <summary>OrderStatus ID for a description (e.g. "Paid"), or null when it does not exist.</summary>
        Task<int?> GetIdAsync(string description);
        Task<InvoiceStatuses> GetInvoiceStatusesAsync();
    }
}
