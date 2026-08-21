namespace WorkNest.Application.Interfaces
{
    /// <summary>Email notification dispatch.</summary>
    public interface IEmailService
    {
        Task SendTourNotificationAsync(string fullName, string email, string phone, string message);
        Task SendQuotationEmailAsync(string email, string customerName, string quotationNumber, byte[]? pdfBytes = null, string? quotationLink = null);
        Task SendChallanEmailAsync(string toEmail, string customerName, string challanNumber, string spaceName, string billingPeriod, decimal totalPayable, DateTime? startOn, DateTime? endOn, decimal totalContractAmount = 0, DateTime? nextBillDueDate = null, decimal balanceLeft = 0, byte[]? pdfBytes = null);
        Task SendBookingConfirmationAsync(string toEmail, string customerName, string bookingNumber, string spaceName, DateTime? startOn, DateTime? endOn, string? billingPeriod = null, decimal totalPayable = 0, decimal currentCycleAmount = 0, decimal securityDeposit = 0, decimal totalContractAmount = 0, DateTime? nextBillDueDate = null, decimal balanceLeft = 0, byte[]? pdfBytes = null);
    }
}
