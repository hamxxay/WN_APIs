namespace WorkNest.Application.Interfaces
{
    /// <summary>Email notification dispatch.</summary>
    public interface IEmailService
    {
        Task SendTourNotificationAsync(string fullName, string email, string phone, string message);
        Task SendQuotationEmailAsync(string email, string customerName, string quotationNumber, byte[]? pdfBytes = null, string? quotationLink = null);
        Task SendChallanEmailAsync(string toEmail, string customerName, string challanNumber, string spaceName, string billingPeriod, decimal totalPayable = 0, DateTime? startOn = null, DateTime? endOn = null, decimal totalContractAmount = 0, DateTime? nextBillDueDate = null, decimal balanceLeft = 0, decimal currentCycleAmount = 0, decimal securityDeposit = 0, decimal taxAmount = 0, decimal discountAmount = 0, byte[]? pdfBytes = null, string? oneBillNumber = null);
        Task SendBookingConfirmationAsync(string toEmail, string customerName, string bookingNumber, string spaceName, DateTime? startOn = null, DateTime? endOn = null, string? billingPeriod = null, decimal totalPayable = 0, decimal currentCycleAmount = 0, decimal securityDeposit = 0, decimal taxAmount = 0, decimal discountAmount = 0, decimal totalContractAmount = 0, DateTime? nextBillDueDate = null, decimal balanceLeft = 0, byte[]? pdfBytes = null);
        Task SendAttendantSurchargeInvoiceEmailAsync(string toEmail, string customerName, string invoiceNumber, string attendantName, string attendantIdNumber, string spaceName, int excessSeatCount, decimal surchargeAmount = 0m, decimal taxAmount = 0m, decimal grandTotal = 0m, string customerAddress = "", DateTime? billingStart = null, DateTime? billingEnd = null, byte[]? pdfBytes = null);
        Task SendAgreementEmailAsync(string toEmail, string customerName, string quotationNumber, byte[] pdfBytes);
        /// <summary>Reminder that a sent agreement still needs the customer's signature. Throws when the email could not be sent.</summary>
        Task SendAgreementReminderEmailAsync(string toEmail, string customerName, string quotationNumber, string? spaceName, DateTime sentDate, int reminderNumber, string portalUrl, byte[]? pdfBytes = null);
        Task SendAnnouncementEmailAsync(string toEmail, string recipientName, string title, string body, string type);
        /// <summary>Sends a ready-made HTML report (e.g. the weekly super admin report). Throws when the email could not be sent.</summary>
        Task SendHtmlReportAsync(string toEmail, string subject, string html);

        /// <summary>Super admin "Send test e-mail" (Email Senders): sends a short test from the given mailbox ("sales" /
        /// "notifications") right away (never held, no fallback to the other mailbox). Returns "address via host:port";
        /// throws with the server's reason when it cannot send.</summary>
        Task<string> SendMailboxTestAsync(string mailbox, string toEmail, string? sentBy) => throw new NotSupportedException("Test e-mails are not supported here.");

        /// <summary>An HTML e-mail of a known type (see <see cref="WorkNest.Application.Services.EmailTypes"/>): the mailbox that sends it is the
        /// one chosen on Administration > Email Senders. Approval types follow the Approval Emails switch, network_alert the Network Alerts one.</summary>
        Task SendTypedEmailAsync(string typeKey, string toEmail, string subject, string html, string fromName) => SendHtmlReportAsync(toEmail, subject, html);
        /// <summary>An Approvals e-mail: sent under <paramref name="fromName"/> and governed by the Approval Emails switch only, not
        /// Email Sending. Switch off: <see cref="EmailSendingOffException"/>, nothing kept. Throws when it could not be sent.</summary>
        Task SendApprovalEmailAsync(string toEmail, string subject, string html, string fromName) => SendHtmlReportAsync(toEmail, subject, html);
        /// <summary>A network alert (access machine / internet / network device down or back): governed by the Network Alerts
        /// switch only (the alert job runs only while it is on), not held by Email Sending. Throws when it could not be sent.</summary>
        Task SendNetworkAlertEmailAsync(string toEmail, string subject, string html) => SendHtmlReportAsync(toEmail, subject, html);
        /// <summary>Sends an e-mail held in Pending emails (its MessageJson) as it was asked for. Throws when it fails.</summary>
        Task SendHeldAsync(string messageJson);
    }
}
