using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.ExternalServices.Email
{
    /// <summary>
    /// Sends email notifications via Gmail SMTP.
    /// Credentials loaded from appsettings.json â€” never hardcoded.
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendTourNotificationAsync(string fullName, string email, string phone, string message)
        {
            var fromEmail = _config["Email:FromEmail"];
            var toEmail   = _config["Email:ToEmail"];
            var password  = _config["Email:GmailAppPassword"];

            if (string.IsNullOrWhiteSpace(fromEmail) ||
                string.IsNullOrWhiteSpace(toEmail) ||
                string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("[EMAIL] Missing email credentials in configuration. Notification skipped.");
                return;
            }

            try
            {
                var body = $"""
                    New Book a Tour Request:

                    Name:    {fullName}
                    Email:   {email}
                    Phone:   {phone}
                    Message: {message}
                    """;

                var mailMessage = new MailMessage
                {
                    From       = new MailAddress(fromEmail),
                    Subject    = $"New Tour Request from {fullName} â€” WorkNest",
                    Body       = body,
                    IsBodyHtml = false,
                };
                mailMessage.To.Add(toEmail);

                using var smtp = new SmtpClient("smtp.gmail.com", 587)
                {
                    Credentials = new NetworkCredential(fromEmail, password),
                    EnableSsl   = true,
                };

                await smtp.SendMailAsync(mailMessage);
                _logger.LogInformation("[EMAIL] Tour notification sent to {To}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EMAIL] Failed to send tour notification.");
            }
        }

        public async Task SendQuotationEmailAsync(string email, string customerName, string quotationNumber, byte[]? pdfBytes = null, string? quotationLink = null)
        {
            var fromEmail = _config["Email:FromEmail"];
            var password  = _config["Email:GmailAppPassword"];

            if (string.IsNullOrWhiteSpace(fromEmail) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("[EMAIL] Missing email credentials or recipient email. Quotation email skipped.");
                return;
            }

            try
            {
                var body = $"""
                    Dear {customerName},

                    Please find attached the quotation for your booking request.

                    Quotation Number: {quotationNumber}
                    
                    Thank you for choosing WorkNest. We look forward to serving you.

                    Best regards,
                    WorkNest Team
                    """;

                var mailMessage = new MailMessage
                {
                    From       = new MailAddress(fromEmail),
                    Subject    = $"Your WorkNest Quotation â€” {quotationNumber}",
                    Body       = body,
                    IsBodyHtml = false,
                };
                mailMessage.To.Add(email);

                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    var attachment = new Attachment(new MemoryStream(pdfBytes), $"{quotationNumber}.pdf", "application/pdf");
                    mailMessage.Attachments.Add(attachment);
                }

                using var smtp = new SmtpClient("smtp.gmail.com", 587)
                {
                    Credentials = new NetworkCredential(fromEmail, password),
                    EnableSsl   = true,
                };

                await smtp.SendMailAsync(mailMessage);
                _logger.LogInformation("[EMAIL] Quotation email sent to {To}", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EMAIL] Failed to send quotation email.");
            }
        }

        public async Task SendChallanEmailAsync(string toEmail, string customerName, string challanNumber, string spaceName, string billingPeriod, decimal totalPayable, DateTime? startOn, DateTime? endOn, decimal totalContractAmount = 0, DateTime? nextBillDueDate = null, decimal balanceLeft = 0, byte[]? pdfBytes = null)
        {
            var fromEmail = _config["Email:FromEmail"];
            var password  = _config["Email:GmailAppPassword"];

            if (string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(toEmail) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("[EMAIL] Missing email credentials or recipient. Challan email skipped.");
                return;
            }

            try
            {
                var body = $"""
                    Dear {customerName},

                    Your challan has been generated for your booking at WorkNest.

                    Challan Number : {challanNumber}
                    Space          : {spaceName}
                    Billing Period : {billingPeriod}
                    Start Date              : {startOn:dd MMM yyyy}
                    End Date                : {endOn:dd MMM yyyy}
                    Total Contract Amount   : PKR {totalContractAmount:N2}
                    Next Bill Due Date      : {nextBillDueDate:dd MMM yyyy}
                    Balance Left            : PKR {balanceLeft:N2}
                    Total Payable (Current) : PKR {totalPayable:N2}

                    Please present this challan at the front desk or use it as a reference for your payment.

                    Thank you for choosing WorkNest.

                    Best regards,
                    WorkNest Team
                    """;

                var mailMessage = new MailMessage
                {
                    From       = new MailAddress(fromEmail),
                    Subject    = $"Your WorkNest Challan â€” {challanNumber}",
                    Body       = body,
                    IsBodyHtml = false,
                };
                mailMessage.To.Add(toEmail);

                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    var attachment = new Attachment(new MemoryStream(pdfBytes), $"Challan-{challanNumber}.pdf", "application/pdf");
                    mailMessage.Attachments.Add(attachment);
                }

                using var smtp = new SmtpClient("smtp.gmail.com", 587)
                {
                    Credentials = new NetworkCredential(fromEmail, password),
                    EnableSsl   = true,
                };

                await smtp.SendMailAsync(mailMessage);
                _logger.LogInformation("[EMAIL] Challan email sent to {To}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EMAIL] Failed to send challan email.");
                throw;
            }
        }

        public async Task SendBookingConfirmationAsync(
            string toEmail, string customerName, string bookingNumber, string spaceName,
            DateTime? startOn, DateTime? endOn, string? billingPeriod = null,
            decimal totalPayable = 0, decimal currentCycleAmount = 0, decimal securityDeposit = 0,
            decimal totalContractAmount = 0, DateTime? nextBillDueDate = null, decimal balanceLeft = 0,
            byte[]? pdfBytes = null)
        {
            var fromEmail = _config["Email:FromEmail"];
            var password  = _config["Email:GmailAppPassword"];

            if (string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(toEmail) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("[EMAIL] Missing email credentials or recipient. Booking confirmation email skipped.");
                return;
            }

            try
            {
                var body = $"""
                    Dear {customerName},

                    Your booking has been confirmed at WorkNest.

                    Booking Reference       : {bookingNumber}
                    Space                   : {spaceName}
                    Contract Start Date     : {startOn:dd MMM yyyy}
                    Contract End Date       : {endOn:dd MMM yyyy}
                    Billing Cycle           : {billingPeriod ?? "N/A"}

                    FINANCIAL SUMMARY:
                    Current Cycle Rent      : PKR {currentCycleAmount:N2}
                    Security Deposit        : PKR {securityDeposit:N2}
                    Total Initial Payable   : PKR {totalPayable:N2}

                    CONTRACT METRICS:
                    Total Contract Amount   : PKR {totalContractAmount:N2}
                    Next Bill Due Date      : {(nextBillDueDate.HasValue ? nextBillDueDate.Value.ToString("dd MMM yyyy") : "N/A")}
                    Balance Left            : PKR {balanceLeft:N2}

                    Please find the booking confirmation PDF attached.

                    Thank you for choosing WorkNest.

                    Best regards,
                    WorkNest Team
                    """;

                var mailMessage = new MailMessage
                {
                    From       = new MailAddress(fromEmail),
                    Subject    = $"Booking Confirmation â€” {bookingNumber} | WorkNest",
                    Body       = body,
                    IsBodyHtml = false,
                };
                mailMessage.To.Add(toEmail);

                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    var attachment = new Attachment(new MemoryStream(pdfBytes), $"Booking-{bookingNumber}.pdf", "application/pdf");
                    mailMessage.Attachments.Add(attachment);
                }

                using var smtp = new SmtpClient("smtp.gmail.com", 587)
                {
                    Credentials = new NetworkCredential(fromEmail, password),
                    EnableSsl   = true,
                };

                await smtp.SendMailAsync(mailMessage);
                _logger.LogInformation("[EMAIL] Booking confirmation email sent to {To}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EMAIL] Failed to send booking confirmation email.");
                throw;
            }
        }
    }
}