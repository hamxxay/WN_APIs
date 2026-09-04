using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.ExternalServices.Email
{
    /// <summary>
    /// Sends email notifications via Gmail SMTP.
    /// Credentials loaded from appsettings.json - never hardcoded.
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
                    Subject    = $"New Tour Request from {fullName} - WorkNest",
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
                    Subject    = $"Your WorkNest Quotation - {quotationNumber}",
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

        public async Task SendChallanEmailAsync(
            string toEmail, string customerName, string challanNumber, string spaceName, string billingPeriod,
            decimal totalPayable, DateTime? startOn, DateTime? endOn, decimal totalContractAmount = 0,
            DateTime? nextBillDueDate = null, decimal balanceLeft = 0, decimal currentCycleAmount = 0,
            decimal securityDeposit = 0, decimal taxAmount = 0, decimal discountAmount = 0, byte[]? pdfBytes = null)
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
                bool isContract = nextBillDueDate.HasValue;
                string periodLabel = isContract ? "Billing Period" : "Billing Basis";
                string rentLabel   = isContract ? "Advance Rent" : "Rent Amount";
                string totalLabel  = isContract ? "Total Payable (Current)" : "Total Payable";

                var sb = new StringBuilder();
                sb.AppendLine($"Dear {customerName},");
                sb.AppendLine();
                sb.AppendLine("Your challan has been generated for your booking at WorkNest.");
                sb.AppendLine();
                sb.AppendLine($"Challan Number          : {challanNumber}");
                sb.AppendLine($"Space                   : {spaceName}");
                sb.AppendLine($"{periodLabel.PadRight(24)}: {billingPeriod}");
                if (startOn.HasValue)
                    sb.AppendLine($"Start Date              : {startOn:dd MMM yyyy}");
                if (endOn.HasValue)
                    sb.AppendLine($"End Date                : {endOn:dd MMM yyyy}");
                sb.AppendLine();
                sb.AppendLine("FINANCIAL BREAKDOWN:");
                sb.AppendLine($"{rentLabel.PadRight(24)}: PKR {currentCycleAmount:N2}");
                if (securityDeposit > 0)
                    sb.AppendLine($"Security Deposit        : PKR {securityDeposit:N2}");
                if (taxAmount > 0)
                    sb.AppendLine($"Sales Tax / PST (16% on Support) : PKR {taxAmount:N2}");
                if (discountAmount > 0)
                    sb.AppendLine($"Discount                : - PKR {discountAmount:N2}");
                sb.AppendLine("--------------------------------------------------");
                sb.AppendLine($"{totalLabel.PadRight(24)}: PKR {totalPayable:N2}");

                if (isContract && nextBillDueDate.HasValue)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Total Contract Amount   : PKR {totalContractAmount:N2}");
                    sb.AppendLine($"Next Bill Due Date      : {nextBillDueDate.Value:dd MMM yyyy}");
                    sb.AppendLine($"Balance Left            : PKR {balanceLeft:N2}");
                }

                if (taxAmount > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("* Note: Rent includes 10% support services; 16% Provincial Sales Tax (PST) is charged on support services.");
                }

                sb.AppendLine();
                sb.AppendLine("Please present this challan at the front desk or use it as a reference for your payment.");
                sb.AppendLine();
                sb.AppendLine("Thank you for choosing WorkNest.");
                sb.AppendLine();
                sb.AppendLine("Best regards,");
                sb.AppendLine("WorkNest Team");

                var body = sb.ToString();

                string docType = challanNumber.StartsWith("INV", StringComparison.OrdinalIgnoreCase) ? "Invoice" : "Challan";
                var mailMessage = new MailMessage
                {
                    From       = new MailAddress(fromEmail),
                    Subject    = $"Your WorkNest {docType} — {challanNumber}",
                    Body       = body,
                    IsBodyHtml = false,
                };
                mailMessage.To.Add(toEmail);

                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    string attachFileName = $"{docType}-{challanNumber}.pdf";
                    var attachment = new Attachment(new MemoryStream(pdfBytes), attachFileName, "application/pdf");
                    mailMessage.Attachments.Add(attachment);
                }
                else
                {
                    _logger.LogWarning("[EMAIL] Warning: {DocType} {Number} email to {To} is being sent WITHOUT PDF attachment (pdfBytes is null or empty).", docType, challanNumber, toEmail);
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
            decimal taxAmount = 0, decimal discountAmount = 0, decimal totalContractAmount = 0,
            DateTime? nextBillDueDate = null, decimal balanceLeft = 0, byte[]? pdfBytes = null)
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
                bool isContract = nextBillDueDate.HasValue;
                string cycleLabel = isContract ? "Billing Cycle" : "Billing Basis";
                string rentLabel  = isContract ? "Advance Rent" : "Rent Amount";
                string totalLabel = isContract ? "Total Initial Payable" : "Total Payable";

                var sb = new StringBuilder();
                sb.AppendLine($"Dear {customerName},");
                sb.AppendLine();
                sb.AppendLine("Your booking has been confirmed at WorkNest.");
                sb.AppendLine();
                sb.AppendLine($"Booking Reference       : {bookingNumber}");
                sb.AppendLine($"Space                   : {spaceName}");
                if (startOn.HasValue)
                    sb.AppendLine($"{(isContract ? "Contract Start Date    " : "Start Date             ")}: {startOn:dd MMM yyyy}");
                if (endOn.HasValue)
                    sb.AppendLine($"{(isContract ? "Contract End Date      " : "End Date               ")}: {endOn:dd MMM yyyy}");
                sb.AppendLine($"{cycleLabel.PadRight(24)}: {billingPeriod ?? "N/A"}");
                sb.AppendLine();
                sb.AppendLine("FINANCIAL SUMMARY:");
                sb.AppendLine($"{rentLabel.PadRight(24)}: PKR {currentCycleAmount:N2}");
                if (securityDeposit > 0)
                    sb.AppendLine($"Security Deposit        : PKR {securityDeposit:N2}");
                if (taxAmount > 0)
                    sb.AppendLine($"Sales Tax / PST (16% on Support) : PKR {taxAmount:N2}");
                if (discountAmount > 0)
                    sb.AppendLine($"Discount                : - PKR {discountAmount:N2}");
                sb.AppendLine("--------------------------------------------------");
                sb.AppendLine($"{totalLabel.PadRight(24)}: PKR {totalPayable:N2}");

                if (isContract && nextBillDueDate.HasValue)
                {
                    sb.AppendLine();
                    sb.AppendLine("CONTRACT METRICS:");
                    sb.AppendLine($"Total Contract Amount   : PKR {totalContractAmount:N2}");
                    sb.AppendLine($"Next Bill Due Date      : {nextBillDueDate.Value:dd MMM yyyy}");
                    sb.AppendLine($"Balance Left            : PKR {balanceLeft:N2}");
                }

                if (taxAmount > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("* Note: Rent includes 10% support services; 16% Provincial Sales Tax (PST) is charged on support services.");
                }

                sb.AppendLine();
                sb.AppendLine("Please find the booking confirmation PDF attached.");
                sb.AppendLine();
                sb.AppendLine("Thank you for choosing WorkNest.");
                sb.AppendLine();
                sb.AppendLine("Best regards,");
                sb.AppendLine("WorkNest Team");

                var body = sb.ToString();

                var mailMessage = new MailMessage
                {
                    From       = new MailAddress(fromEmail),
                    Subject    = $"Booking Confirmation - {bookingNumber} | WorkNest",
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