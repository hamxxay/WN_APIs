namespace WorkNest.Application.Interfaces
{
    /// <summary>Email notification dispatch.</summary>
    public interface IEmailService
    {
        Task SendTourNotificationAsync(string fullName, string email, string phone, string message);
        Task SendQuotationEmailAsync(string email, string customerName, string quotationNumber, byte[]? pdfBytes = null, string? quotationLink = null);
        Task SendChallanEmailAsync(string toEmail, string customerName, string challanNumber, string spaceName, string billingPeriod, decimal totalPayable, DateTime? startOn, DateTime? endOn, decimal totalContractAmount = 0, DateTime? nextBillDueDate = null, decimal balanceLeft = 0, decimal currentCycleAmount = 0, decimal securityDeposit = 0, decimal taxAmount = 0, decimal discountAmount = 0, byte[]? pdfBytes = null);
        Task SendBookingConfirmationAsync(string toEmail, string customerName, string bookingNumber, string spaceName, DateTime? startOn, DateTime? endOn, string? billingPeriod = null, decimal totalPayable = 0, decimal currentCycleAmount = 0, decimal securityDeposit = 0, decimal taxAmount = 0, decimal discountAmount = 0, decimal totalContractAmount = 0, DateTime? nextBillDueDate = null, decimal balanceLeft = 0, byte[]? pdfBytes = null);
        Task SendAttendantSurchargeInvoiceEmailAsync(string toEmail, string customerName, string invoiceNumber, string attendantName, string attendantIdNumber, string spaceName, int excessSeatCount, decimal surchargeAmount, decimal taxAmount = 0m, decimal grandTotal = 0m, string customerAddress = "", DateTime? billingStart = null, DateTime? billingEnd = null, byte[]? pdfBytes = null);
        Task SendAgreementEmailAsync(string toEmail, string customerName, string quotationNumber, byte[] pdfBytes);
        /// <summary>Reminder that a sent agreement still needs the customer's signature. Throws when the email could not be sent.</summary>
        Task SendAgreementReminderEmailAsync(string toEmail, string customerName, string quotationNumber, string? spaceName, DateTime sentDate, int reminderNumber, string portalUrl, byte[]? pdfBytes = null);
        Task SendAnnouncementEmailAsync(string toEmail, string recipientName, string title, string body, string type);
    }
}
