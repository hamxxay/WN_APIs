using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.ExternalServices.Email
{
    /// <summary>
    /// Sends email notifications via Gmail SMTP.
    /// Mirrors the Python send_tour_notification() function exactly.
    /// Credentials loaded from appsettings.json — never hardcoded.
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

        /// <summary>
        /// Sends a tour booking notification email to the configured recipient.
        /// Mirrors Python send_tour_notification() — same subject and body format.
        /// </summary>
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
                    Subject    = $"New Tour Request from {fullName} — WorkNest",
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

        /// <summary>
        /// Sends a quotation email to the customer with optional PDF attachment.
        /// </summary>
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
                    Subject    = $"Your WorkNest Quotation — {quotationNumber}",
                    Body       = body,
                    IsBodyHtml = false,
                };
                mailMessage.To.Add(email);

                // Add PDF attachment if provided
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
        public async Task SendChallanEmailAsync(string toEmail, string customerName, string challanNumber, string spaceName, string billingPeriod, decimal totalPayable, DateTime? startOn, DateTime? endOn, byte[]? pdfBytes = null)
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
                    Start Date     : {startOn:dd MMM yyyy}
                    End Date       : {endOn:dd MMM yyyy}
                    Total Payable  : PKR {totalPayable:N2}

                    Please present this challan at the front desk or use it as a reference for your payment.

                    Thank you for choosing WorkNest.

                    Best regards,
                    WorkNest Team
                    """;

                var mailMessage = new MailMessage
                {
                    From       = new MailAddress(fromEmail),
                    Subject    = $"Your WorkNest Challan — {challanNumber}",
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

        public async Task SendBookingConfirmationAsync(string toEmail, string customerName, string bookingNumber, string spaceName, DateTime? startOn, DateTime? endOn, byte[]? pdfBytes = null)
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

                    Booking Reference : {bookingNumber}
                    Space             : {spaceName}
                    Start Date        : {startOn:dd MMM yyyy}
                    End Date          : {endOn:dd MMM yyyy}

                    Please find the booking confirmation attached.

                    Thank you for choosing WorkNest.

                    Best regards,
                    WorkNest Team
                    """;

                var mailMessage = new MailMessage
                {
                    From       = new MailAddress(fromEmail),
                    Subject    = $"Booking Confirmation — {bookingNumber} | WorkNest",
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
