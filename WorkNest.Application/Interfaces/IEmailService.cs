namespace WorkNest.Application.Interfaces
{
    /// <summary>Email notification dispatch.</summary>
    public interface IEmailService
    {
        Task SendTourNotificationAsync(string fullName, string email, string phone, string message);
        Task SendQuotationEmailAsync(string email, string customerName, string quotationNumber, byte[]? pdfBytes = null, string? quotationLink = null);
        Task SendChallanEmailAsync(string toEmail, string customerName, string challanNumber, string spaceName, string billingPeriod, decimal totalPayable, DateTime? startOn, DateTime? endOn, byte[]? pdfBytes = null);
        Task SendBookingConfirmationAsync(string toEmail, string customerName, string bookingNumber, string spaceName, DateTime? startOn, DateTime? endOn, byte[]? pdfBytes = null);
    }
}
