using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WorkNest.Application.DTOs.EmailOutbox;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.Infrastructure.ExternalServices.Email
{
    /// <summary>
    /// Sends email notifications via configured SMTP (e.g., Zoho Mail / Gmail).
    /// Credentials loaded from appsettings.json - never hardcoded.
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;
        private readonly IServiceSwitch? _serviceSwitch;
        private readonly IEmailOutbox? _outbox;

        public EmailService(IConfiguration config, ILogger<EmailService> logger, IServiceSwitch? serviceSwitch = null, IEmailOutbox? outbox = null)
        {
            _config = config;
            _logger = logger;
            _serviceSwitch = serviceSwitch;
            _outbox = outbox;
        }

        /// <summary>Email Sending is off and the e-mail could not be held either (SS_02 not run): nothing was e-mailed.</summary>
        public const string EmailOffMessage = "Email sending is turned off (Administration > Services > Email Sending). Nothing was e-mailed.";

        /// <summary>
        /// The Email Sending switch (CMS Services, <see cref="BackgroundServiceCatalog.EmailSending"/>). Fails open like every
        /// switch (true without the row / when the database cannot be read).
        /// </summary>
        private async Task<bool> EmailOnAsync()
        {
            if (_serviceSwitch == null) return true;
            try { return await _serviceSwitch.IsEnabledAsync(BackgroundServiceCatalog.EmailSending); }
            catch (Exception) { return true; }
        }

        /// <summary>
        /// Every e-mail goes out here. Email Sending on: sent now. Off: held whole in Pending emails (dbo.WN_EmailOutbox)
        /// and <see cref="EmailHeldException"/> is thrown, so the caller reports it and does not queue its own retry; it goes
        /// out when the switch is on again unless a super admin discards it. Off without SS_02: refused, nothing kept.
        /// </summary>
        private async Task DeliverAsync(SmtpClient smtp, MailMessage mail, string kind)
        {
            if (await EmailOnAsync())
            {
                await smtp.SendMailAsync(mail);
                return;
            }
            if (_outbox == null)
                throw new EmailSendingOffException(EmailOffMessage);
            int id;
            try
            {
                id = await _outbox.AddAsync(kind, string.Join(", ", mail.To.Select(a => a.Address)), mail.Subject ?? "", ToJson(mail));
            }
            catch (EmailOutboxMissingException)
            {
                _logger.LogWarning("[EMAIL] {Kind} not sent: Email Sending is off and SS_02 (Pending emails) is not installed.", kind);
                throw new EmailSendingOffException(EmailOffMessage);
            }
            _logger.LogInformation("[EMAIL] {Kind} to {To} held as pending e-mail #{Id}: Email Sending is off.", kind, string.Join(", ", mail.To.Select(a => a.Address)), id);
            throw new EmailHeldException(id);
        }

        private static string ToJson(MailMessage mail)
        {
            var held = new HeldEmail
            {
                FromName = mail.From?.DisplayName ?? "",
                To = mail.To.Select(a => a.ToString()).ToList(),
                Cc = mail.CC.Select(a => a.ToString()).ToList(),
                Bcc = mail.Bcc.Select(a => a.ToString()).ToList(),
                ReplyTo = mail.ReplyToList.Select(a => a.ToString()).ToList(),
                Subject = mail.Subject ?? "",
                Body = mail.Body ?? "",
                IsHtml = mail.IsBodyHtml
            };
            foreach (var a in mail.Attachments)
            {
                using var ms = new MemoryStream();
                if (a.ContentStream.CanSeek) a.ContentStream.Position = 0;
                a.ContentStream.CopyTo(ms);
                if (a.ContentStream.CanSeek) a.ContentStream.Position = 0;
                held.Attachments.Add(new HeldAttachment
                {
                    FileName = a.Name ?? "attachment",
                    ContentType = a.ContentType?.MediaType ?? "application/octet-stream",
                    Base64 = Convert.ToBase64String(ms.ToArray())
                });
            }
            return JsonSerializer.Serialize(held);
        }

        /// <summary>Sends a held e-mail (Pending emails) as it was asked for, from the current Email settings. Throws when it fails.</summary>
        public async Task SendHeldAsync(string messageJson)
        {
            var held = JsonSerializer.Deserialize<HeldEmail>(messageJson) ?? throw new InvalidOperationException("The held e-mail could not be read.");
            var (fromEmail, password, host, port) = GetEmailSettings();
            if (string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Email is not set up on the server (Email:FromEmail / Password).");
            if (held.To.Count == 0)
                throw new InvalidOperationException("The held e-mail has no recipient.");

            using var mail = new MailMessage
            {
                From = new MailAddress(fromEmail, string.IsNullOrWhiteSpace(held.FromName) ? null : held.FromName),
                Subject = held.Subject,
                Body = held.Body,
                IsBodyHtml = held.IsHtml
            };
            foreach (var a in held.To) mail.To.Add(a);
            foreach (var a in held.Cc) mail.CC.Add(a);
            foreach (var a in held.Bcc) mail.Bcc.Add(a);
            foreach (var a in held.ReplyTo) mail.ReplyToList.Add(a);
            foreach (var a in held.Attachments)
                mail.Attachments.Add(new Attachment(new MemoryStream(Convert.FromBase64String(a.Base64)), a.FileName, a.ContentType));

            using var smtp = CreateSmtpClient(fromEmail, password, host, port);
            await smtp.SendMailAsync(mail);
        }

        private (string fromEmail, string password, string host, int port) GetEmailSettings()
        {
            var fromEmail = _config["Email:FromEmail"] ?? string.Empty;
            var password  = _config["Email:Password"] ?? _config["Email:GmailAppPassword"] ?? string.Empty;
            var host      = _config["Email:SmtpHost"] ?? "smtppro.zoho.com";
            var port      = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587;
            return (fromEmail, password, host, port);
        }

        private SmtpClient CreateSmtpClient(string fromEmail, string password, string host, int port)
        {
            return new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(fromEmail, password),
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };
        }

        public async Task SendTourNotificationAsync(string fullName, string email, string phone, string message)
        {
            var (fromEmail, password, host, port) = GetEmailSettings();
            // Tour requests go to sales: Email:TourRequestsTo, else Email:ToEmail, else sales@worknestpk.com.
            // (An empty setting used to leave no recipient, so no email was sent at all.)
            var toEmail = new[] { _config["Email:TourRequestsTo"], _config["Email:ToEmail"] }
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "sales@worknestpk.com";

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
                    From       = new MailAddress(fromEmail, "WorkNest Tours"),
                    Subject    = $"New Tour Request from {fullName} - WorkNest",
                    Body       = body,
                    IsBodyHtml = false,
                };
                foreach (var to in toEmail.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    mailMessage.To.Add(to);
                // Sales can reply straight to the customer.
                if (!string.IsNullOrWhiteSpace(email) && MailAddress.TryCreate(email.Trim(), out var replyTo))
                    mailMessage.ReplyToList.Add(replyTo);

                using var smtp = CreateSmtpClient(fromEmail, password, host, port);
                await DeliverAsync(smtp, mailMessage, "Tour request notification");   // Email Sending off: held in Pending emails
                _logger.LogInformation("[EMAIL] Tour notification sent to {To} via {Host}:{Port}", toEmail, host, port);
            }
            catch (Exception ex) when (ex is not EmailHeldException and not EmailSendingOffException)
            {
                _logger.LogError(ex, "[EMAIL] Failed to send tour notification.");
            }
        }

        public async Task SendQuotationEmailAsync(string email, string customerName, string quotationNumber, byte[]? pdfBytes = null, string? quotationLink = null)
        {
            var (fromEmail, password, host, port) = GetEmailSettings();

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
                    From       = new MailAddress(fromEmail, "WorkNest"),
                    Subject    = $"Your Work-Place Solution - {customerName}",
                    Body       = body,
                    IsBodyHtml = false,
                };
                mailMessage.To.Add(email);

                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    var attachment = new Attachment(new MemoryStream(pdfBytes), $"{quotationNumber}.pdf", "application/pdf");
                    mailMessage.Attachments.Add(attachment);
                }

                using var smtp = CreateSmtpClient(fromEmail, password, host, port);
                await DeliverAsync(smtp, mailMessage, "Quotation e-mail");   // Email Sending off: held in Pending emails
                _logger.LogInformation("[EMAIL] Quotation email sent to {To} via {Host}:{Port}", email, host, port);
            }
            catch (Exception ex) when (ex is not EmailHeldException and not EmailSendingOffException)
            {
                _logger.LogError(ex, "[EMAIL] Failed to send quotation email.");
            }
        }

        public async Task SendChallanEmailAsync(
            string toEmail, string customerName, string challanNumber, string spaceName, string billingPeriod,
            decimal totalPayable = 0, DateTime? startOn = null, DateTime? endOn = null, decimal totalContractAmount = 0,
            DateTime? nextBillDueDate = null, decimal balanceLeft = 0, decimal currentCycleAmount = 0,
            decimal securityDeposit = 0, decimal taxAmount = 0, decimal discountAmount = 0, byte[]? pdfBytes = null, string? oneBillNumber = null)
        {
            var (fromEmail, password, host, port) = GetEmailSettings();

            if (string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(toEmail) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("[EMAIL] Missing email credentials or recipient. Challan email not sent.");
                // Throw, so callers (and the retry queue) never record this as "Sent".
                throw new InvalidOperationException("The email could not be sent: email is not set up on the server, or the customer has no email address.");
            }

            try
            {
                bool isContract = nextBillDueDate.HasValue;
                string periodLabel = isContract ? "Billing Period" : "Billing Basis";

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
                if (nextBillDueDate.HasValue)
                    sb.AppendLine($"Next Bill Due Date      : {nextBillDueDate.Value:dd MMM yyyy}");
                if (!string.IsNullOrWhiteSpace(oneBillNumber))
                {
                    sb.AppendLine();
                    sb.AppendLine($"1Bill Consumer Number   : {oneBillNumber}");
                    sb.AppendLine("Pay with 1Bill from any bank app, ATM or bank branch using this consumer number.");
                }
                sb.AppendLine();
                sb.AppendLine("Please find the attachment below.");
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
                    From       = new MailAddress(fromEmail, "WorkNest Billing"),
                    Subject    = $"Your WorkNest {docType} - {challanNumber}",
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

                using var smtp = CreateSmtpClient(fromEmail, password, host, port);
                await DeliverAsync(smtp, mailMessage, "Invoice / challan e-mail");   // Email Sending off: held in Pending emails
                _logger.LogInformation("[EMAIL] Challan email sent to {To} via {Host}:{Port}", toEmail, host, port);
            }
            catch (Exception ex) when (ex is not EmailHeldException and not EmailSendingOffException)
            {
                _logger.LogError(ex, "[EMAIL] Failed to send challan email.");
                throw;
            }
        }

        public async Task SendBookingConfirmationAsync(
            string toEmail, string customerName, string bookingNumber, string spaceName,
            DateTime? startOn = null, DateTime? endOn = null, string? billingPeriod = null,
            decimal totalPayable = 0, decimal currentCycleAmount = 0, decimal securityDeposit = 0,
            decimal taxAmount = 0, decimal discountAmount = 0, decimal totalContractAmount = 0,
            DateTime? nextBillDueDate = null, decimal balanceLeft = 0, byte[]? pdfBytes = null)
        {
            var (fromEmail, password, host, port) = GetEmailSettings();

            if (string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(toEmail) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("[EMAIL] Missing email credentials or recipient. Booking confirmation email skipped.");
                return;
            }

            try
            {
                bool isContract = nextBillDueDate.HasValue;
                string cycleLabel = isContract ? "Billing Cycle" : "Billing Basis";

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
                if (nextBillDueDate.HasValue)
                    sb.AppendLine($"Next Bill Due Date      : {nextBillDueDate.Value:dd MMM yyyy}");
                sb.AppendLine($"{cycleLabel.PadRight(24)}: {billingPeriod ?? "N/A"}");
                sb.AppendLine();
                sb.AppendLine("Please find the attachment below.");
                sb.AppendLine();
                sb.AppendLine("Thank you for choosing WorkNest.");
                sb.AppendLine();
                sb.AppendLine("Best regards,");
                sb.AppendLine("WorkNest Team");

                var body = sb.ToString();

                var mailMessage = new MailMessage
                {
                    From       = new MailAddress(fromEmail, "WorkNest Bookings"),
                    Subject    = $"Booking Confirmation - {spaceName} | WorkNest",
                    Body       = body,
                    IsBodyHtml = false,
                };
                mailMessage.To.Add(toEmail);

                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    var attachment = new Attachment(new MemoryStream(pdfBytes), $"Booking-{bookingNumber}.pdf", "application/pdf");
                    mailMessage.Attachments.Add(attachment);
                }

                using var smtp = CreateSmtpClient(fromEmail, password, host, port);
                await DeliverAsync(smtp, mailMessage, "Booking confirmation e-mail");   // Email Sending off: held in Pending emails
                _logger.LogInformation("[EMAIL] Booking confirmation email sent to {To} via {Host}:{Port}", toEmail, host, port);
            }
            catch (Exception ex) when (ex is not EmailHeldException and not EmailSendingOffException)
            {
                _logger.LogError(ex, "[EMAIL] Failed to send booking confirmation email.");
                throw;
            }
        }

        public async Task SendAttendantSurchargeInvoiceEmailAsync(
            string toEmail, string customerName, string invoiceNumber, string attendantName,
            string attendantIdNumber, string spaceName, int excessSeatCount, decimal surchargeAmount,
            decimal taxAmount = 0m, decimal grandTotal = 0m, string customerAddress = "",
            DateTime? billingStart = null, DateTime? billingEnd = null, byte[]? pdfBytes = null)
        {
            var (fromEmail, password, host, port) = GetEmailSettings();

            if (string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(toEmail) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("[EMAIL] Missing email credentials or recipient. Surcharge invoice email not sent.");
                throw new InvalidOperationException("The surcharge invoice email could not be sent: email is not set up on the server, or the customer has no email address.");
            }

            decimal supportCharge = (taxAmount > 0) 
                ? Math.Round(taxAmount / 0.16m, 2) 
                : (excessSeatCount > 0 ? (2000.00m * excessSeatCount) : Math.Round(surchargeAmount * 0.10m, 2));
            if (taxAmount <= 0) taxAmount = Math.Round(supportCharge * 0.16m, 2);
            if (grandTotal <= 0) grandTotal = surchargeAmount + taxAmount;

            if (pdfBytes == null || pdfBytes.Length == 0)
            {
                try
                {
                    var pdfDto = new WorkNest.Infrastructure.ExternalServices.Pdf.StatementInvoicePdfDto
                    {
                        InvoiceNumber = invoiceNumber,
                        AccountName = customerName,
                        AttnName = attendantName,
                        BillingAddress = customerAddress ?? "",
                        InvoiceDate = WorkNest.Application.Services.BusinessClock.Default.Today,
                        DueDate = WorkNest.Application.Services.BusinessClock.Default.Today.AddDays(7),
                        BillingPeriodStart = billingStart,
                        BillingPeriodEnd = billingEnd,
                        CurrentInvoiceTotal = grandTotal,
                        CurrencyCode = "PKR",
                        VatRate = 0.16m,
                        AppliedChargePercentage = 10.00m,
                        AppliedTaxPercentage = 16.00m,
                        LineItems = new List<WorkNest.Infrastructure.ExternalServices.Pdf.StatementInvoiceLineItemDto>
                        {
                            new WorkNest.Infrastructure.ExternalServices.Pdf.StatementInvoiceLineItemDto
                            {
                                Description = $"Office Rent Capacity Overage Surcharge - {spaceName} ({excessSeatCount} excess seat{(excessSeatCount > 1 ? "s" : "")} for {attendantName})",
                                FromDate = billingStart ?? WorkNest.Application.Services.BusinessClock.Default.Today,
                                ToDate = billingEnd ?? WorkNest.Application.Services.BusinessClock.Default.Today.AddMonths(1),
                                PriceExclVat = surchargeAmount,
                                VatAmount = taxAmount,
                                Category = "Office Rent Surcharge"
                            }
                        }
                    };
                    pdfBytes = WorkNest.Infrastructure.ExternalServices.Pdf.StatementInvoicePdfGenerator.GeneratePdf(pdfDto);
                }
                catch (Exception pdfEx)
                {
                    _logger.LogError(pdfEx, "[PDF] Failed to compile StatementInvoicePdfGenerator PDF for surcharge invoice #{InvoiceNumber}.", invoiceNumber);
                }
            }

            try
            {
                string encCustomerName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(customerName) ? "Valued Customer" : customerName);
                string encSpaceName = WebUtility.HtmlEncode(spaceName ?? "");
                string encAttendantName = WebUtility.HtmlEncode(attendantName ?? "");
                string encAttendantIdNumber = WebUtility.HtmlEncode(attendantIdNumber ?? "");
                string encInvoiceNumber = WebUtility.HtmlEncode(invoiceNumber ?? "");

                var mail = new MailMessage();
                mail.From = new MailAddress(fromEmail, "WorkNest Billing");
                mail.To.Add(toEmail);
                mail.Subject = $"WorkNest Custom Invoice - Capacity Overage Surcharge";
                mail.IsBodyHtml = true;

                string body = $@"
                <div style=""font-family: Arial, sans-serif; color: #1e293b; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 10px;"">
                    <h2 style=""color: #0ea5e9; margin-top: 0;"">WorkNest Custom Invoice</h2>
                    <p>Dear <strong>{encCustomerName}</strong>,</p>
                    <p>An over-capacity attendant assignment has been recorded for your booking space at WorkNest. Below are the custom invoice details:</p>
                    
                    <table style=""width: 100%; border-collapse: collapse; margin: 20px 0; background: #f8fafc; border-radius: 8px;"">
                        <tr><td style=""padding: 10px; border-bottom: 1px solid #e2e8f0;""><strong>Invoice Number:</strong></td><td style=""padding: 10px; border-bottom: 1px solid #e2e8f0;"">{encInvoiceNumber}</td></tr>
                        <tr><td style=""padding: 10px; border-bottom: 1px solid #e2e8f0;""><strong>Space Name:</strong></td><td style=""padding: 10px; border-bottom: 1px solid #e2e8f0;"">{encSpaceName}</td></tr>
                        <tr><td style=""padding: 10px; border-bottom: 1px solid #e2e8f0;""><strong>Attendant Name:</strong></td><td style=""padding: 10px; border-bottom: 1px solid #e2e8f0;"">{encAttendantName} (ID: {encAttendantIdNumber})</td></tr>
                        <tr><td style=""padding: 10px;""><strong>Excess Seat Count:</strong></td><td style=""padding: 10px;"">{excessSeatCount} seat(s) over capacity</td></tr>
                    </table>

                    <p>Please find the attachment below.</p>
                    <p>Thank you for choosing WorkNest.</p>
                    <hr style=""border: none; border-top: 1px solid #e2e8f0; margin-top: 30px;"" />
                    <p style=""font-size: 0.8em; color: #94a3b8; text-align: center;"">WorkNest Co-working & Office Spaces | Automated Billing System</p>
                </div>";

                mail.Body = body;

                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    mail.Attachments.Add(new Attachment(new MemoryStream(pdfBytes), $"Custom-Invoice-{invoiceNumber}.pdf", "application/pdf"));
                }

                using var smtp = CreateSmtpClient(fromEmail, password, host, port);
                await DeliverAsync(smtp, mail, "Surcharge invoice e-mail");   // Email Sending off: held in Pending emails
                _logger.LogInformation("[EMAIL] Custom surcharge invoice email #{InvoiceNumber} with Statement PDF sent successfully to {ToEmail} via {Host}:{Port}.", invoiceNumber, toEmail, host, port);
            }
            catch (Exception ex) when (ex is not EmailHeldException and not EmailSendingOffException)
            {
                _logger.LogError(ex, "[EMAIL] Failed to send custom surcharge invoice email #{InvoiceNumber} to {ToEmail}.", invoiceNumber, toEmail);
                throw;
            }
        }

        public async Task SendAgreementEmailAsync(string toEmail, string customerName, string quotationNumber, byte[] pdfBytes)
        {
            var (fromEmail, password, host, port) = GetEmailSettings();

            if (string.IsNullOrWhiteSpace(fromEmail) ||
                string.IsNullOrWhiteSpace(toEmail) ||
                string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("[EMAIL] Missing email credentials or recipient email. Agreement email not sent.");
                // Throw, so the agreement is marked EmailFailed instead of looking sent.
                throw new InvalidOperationException("The agreement email could not be sent: email is not set up on the server, or the customer has no email address.");
            }

            try
            {
                string encCustomerName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(customerName) ? "Valued Customer" : customerName);
                string encQuotationNumber = WebUtility.HtmlEncode(quotationNumber ?? "");
                // Customers sign on the public website (worknestpk.com/my-agreements), same link as the reminders.
                string portalLink = WebUtility.HtmlEncode(WorkNest.Application.DTOs.Agreement.AgreementReminderOptions.DefaultPortalUrl);

                using var mail = new MailMessage();
                mail.From = new MailAddress(fromEmail, "WorkNest Office Spaces");
                mail.To.Add(new MailAddress(toEmail));
                mail.Subject = $"WorkNest - Coworking Space Use Agreement ({customerName})";
                mail.IsBodyHtml = true;

                string body = $@"
                <div style=""font-family: Arial, sans-serif; color: #1e293b; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 8px;"">
                    <h2 style=""color: #2563eb; margin-top: 0;"">Coworking Space Use Agreement</h2>
                    <p>Dear <strong>{encCustomerName}</strong>,</p>
                    <p>Please find attached your Coworking Space Use Agreement for quotation <strong>{encQuotationNumber}</strong>.</p>
                    <p>You can review and sign it online: sign in at worknestpk.com with this email address and open <strong>My Agreements</strong>. You can also print it, sign it and upload the signed copy there.</p>
                    <p style=""text-align: center; margin: 24px 0;"">
                        <a href=""{portalLink}"" style=""background: #0f766e; color: #ffffff; padding: 12px 22px; border-radius: 8px; text-decoration: none; font-weight: bold;"">View and sign your agreement</a>
                    </p>
                    <p>Thank you for partnering with WorkNest.</p>
                    <hr style=""border: none; border-top: 1px solid #e2e8f0; margin-top: 30px;"" />
                    <p style=""font-size: 0.8em; color: #94a3b8; text-align: center;"">WorkNest Co-working & Office Spaces | Automated System</p>
                </div>";

                mail.Body = body;

                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    mail.Attachments.Add(new Attachment(new MemoryStream(pdfBytes), $"Agreement-{quotationNumber}.pdf", "application/pdf"));
                }

                using var smtp = CreateSmtpClient(fromEmail, password, host, port);
                await DeliverAsync(smtp, mail, "Agreement e-mail");   // Email Sending off: held in Pending emails
                _logger.LogInformation("[EMAIL] Agreement PDF email for quotation #{QuotationNumber} sent successfully to {ToEmail} via {Host}:{Port}.", quotationNumber, toEmail, host, port);
            }
            catch (Exception ex) when (ex is not EmailHeldException and not EmailSendingOffException)
            {
                _logger.LogError(ex, "[EMAIL] Failed to send agreement email for quotation #{QuotationNumber} to {ToEmail}.", quotationNumber, toEmail);
                throw;
            }
        }

        public async Task SendAgreementReminderEmailAsync(string toEmail, string customerName, string quotationNumber, string? spaceName, DateTime sentDate, int reminderNumber, string portalUrl, byte[]? pdfBytes = null)
        {
            var (fromEmail, password, host, port) = GetEmailSettings();

            // Unlike the other senders this one throws when it cannot send, so the reminder job does not record
            // a reminder that never left (it retries on a later run instead).
            if (string.IsNullOrWhiteSpace(fromEmail) ||
                string.IsNullOrWhiteSpace(toEmail) ||
                string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("[EMAIL] Missing email credentials or recipient email. Agreement reminder email skipped.");
                throw new InvalidOperationException("Email credentials or recipient email missing; agreement reminder not sent.");
            }

            try
            {
                string name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(customerName) ? "Valued Customer" : customerName);
                string qNum = WebUtility.HtmlEncode(quotationNumber);
                string space = string.IsNullOrWhiteSpace(spaceName) ? "" : WebUtility.HtmlEncode(spaceName);
                string link = WebUtility.HtmlEncode(portalUrl);
                string sentOn = sentDate.ToString("dd MMM yyyy");
                string spaceLine = string.IsNullOrEmpty(space) ? "" : $@"<p style=""margin: 4px 0;""><strong>Space:</strong> {space}</p>";
                string intro = reminderNumber > 1
                    ? "This is a final friendly reminder that your WorkNest agreement is still waiting for your signature."
                    : "This is a friendly reminder that your WorkNest agreement is waiting for your signature.";

                using var mail = new MailMessage();
                mail.From = new MailAddress(fromEmail, "WorkNest Office Spaces");
                mail.To.Add(new MailAddress(toEmail));
                mail.Subject = $"Reminder: Your WorkNest agreement is waiting for your signature ({quotationNumber})";
                mail.IsBodyHtml = true;

                string body = $@"
                <div style=""font-family: Arial, sans-serif; color: #1e293b; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 8px;"">
                    <h2 style=""color: #2563eb; margin-top: 0;"">Your WorkNest agreement is waiting for your signature</h2>
                    <p>Dear <strong>{name}</strong>,</p>
                    <p>{intro}</p>
                    <div style=""background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 12px 16px; margin: 16px 0;"">
                        <p style=""margin: 4px 0;""><strong>Quotation:</strong> {qNum}</p>
                        {spaceLine}
                        <p style=""margin: 4px 0;""><strong>Agreement sent on:</strong> {sentOn}</p>
                    </div>
                    <p><strong>To complete your booking:</strong></p>
                    <ol style=""padding-left: 20px; line-height: 1.6;"">
                        <li>Download your agreement from <strong>My Agreements</strong>{(pdfBytes != null && pdfBytes.Length > 0 ? " (a copy is also attached to this email)" : "")}.</li>
                        <li>Sign it.</li>
                        <li>Upload the signed copy on the same My Agreements page, or simply reply to this email with the signed copy attached.</li>
                    </ol>
                    <p style=""text-align: center; margin: 24px 0;"">
                        <a href=""{link}"" style=""background: #2563eb; color: #ffffff; text-decoration: none; padding: 10px 22px; border-radius: 6px; display: inline-block;"">Open My Agreements</a>
                    </p>
                    <p style=""font-size: 0.9em; color: #64748b;"">If the button does not work, copy this link into your browser: <a href=""{link}"">{link}</a></p>
                    <p>If you have already sent the signed agreement, please ignore this reminder. If you have any questions, just reply to this email.</p>
                    <p>Thank you for choosing WorkNest.</p>
                    <hr style=""border: none; border-top: 1px solid #e2e8f0; margin-top: 30px;"" />
                    <p style=""font-size: 0.8em; color: #94a3b8; text-align: center;"">WorkNest Co-working &amp; Office Spaces &bull; Automated System</p>
                </div>";

                mail.Body = body;

                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    mail.Attachments.Add(new Attachment(new MemoryStream(pdfBytes), $"Agreement-{quotationNumber}.pdf", "application/pdf"));
                }

                using var smtp = CreateSmtpClient(fromEmail, password, host, port);
                await DeliverAsync(smtp, mail, "Agreement reminder e-mail");   // Email Sending off: held in Pending emails
                _logger.LogInformation("[EMAIL] Agreement reminder #{ReminderNumber} for quotation #{QuotationNumber} sent to {ToEmail} via {Host}:{Port}.", reminderNumber, quotationNumber, toEmail, host, port);
            }
            catch (Exception ex) when (ex is not EmailHeldException and not EmailSendingOffException)
            {
                _logger.LogError(ex, "[EMAIL] Failed to send agreement reminder #{ReminderNumber} for quotation #{QuotationNumber} to {ToEmail}.", reminderNumber, quotationNumber, toEmail);
                throw;
            }
        }

        public async Task SendAnnouncementEmailAsync(string toEmail, string recipientName, string title, string body, string type)
        {
            var (fromEmail, password, host, port) = GetEmailSettings();

            if (string.IsNullOrWhiteSpace(fromEmail) ||
                string.IsNullOrWhiteSpace(toEmail) ||
                string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("[EMAIL] Missing email credentials or recipient email. Announcement email skipped for {ToEmail}.", toEmail);
                return;
            }

            try
            {
                using var mail = new MailMessage();
                mail.From = new MailAddress(fromEmail, "WorkNest Operations");
                mail.To.Add(new MailAddress(toEmail));

                bool isAlert = string.Equals(type, "Alert", StringComparison.OrdinalIgnoreCase);
                mail.Subject = isAlert ? $"[URGENT ALERT] {title} - WorkNest" : $"{title} - WorkNest Announcement";
                mail.IsBodyHtml = true;

                string badgeColor = isAlert ? "#ef4444" : "#2563eb";
                string badgeBg = isAlert ? "#fef2f2" : "#eff6ff";
                string badgeBorder = isAlert ? "#fca5a5" : "#bfdbfe";
                string headerTitle = isAlert ? "IMPORTANT ALERT" : "ANNOUNCEMENT";
                string formattedBody = System.Web.HttpUtility.HtmlEncode(body).Replace("\n", "<br/>");

                string html = $@"
                <div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; color: #1e293b; max-width: 620px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 12px; background-color: #ffffff;"">
                    <div style=""display: inline-block; padding: 4px 12px; border-radius: 9999px; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.05em; color: {badgeColor}; background-color: {badgeBg}; border: 1px solid {badgeBorder}; margin-bottom: 12px;"">
                        {headerTitle}
                    </div>
                    <h1 style=""color: #0f172a; font-size: 20px; font-weight: 700; margin-top: 0; margin-bottom: 16px; line-height: 1.3;"">{System.Web.HttpUtility.HtmlEncode(title)}</h1>
                    <p style=""font-size: 14px; color: #475569; margin-bottom: 20px;"">Hello <strong>{System.Web.HttpUtility.HtmlEncode(recipientName)}</strong>,</p>
                    <div style=""font-size: 15px; line-height: 1.6; color: #334155; padding: 16px; background-color: #f8fafc; border-left: 4px solid {badgeColor}; border-radius: 4px; margin-bottom: 24px;"">
                        {formattedBody}
                    </div>
                    <p style=""font-size: 13px; color: #64748b; margin-bottom: 4px;"">Need assistance? Reach out to our community operations desk or reply to this email.</p>
                    <hr style=""border: none; border-top: 1px solid #e2e8f0; margin: 24px 0 16px 0;"" />
                    <p style=""font-size: 12px; color: #94a3b8; text-align: center; margin: 0;"">WorkNest Operations & Control Center | Sent automatically</p>
                </div>";

                mail.Body = html;

                using var smtp = CreateSmtpClient(fromEmail, password, host, port);
                await DeliverAsync(smtp, mail, "Announcement e-mail");   // Email Sending off: held in Pending emails
                _logger.LogInformation("[EMAIL] Announcement email '{Title}' sent successfully to {ToEmail} via {Host}:{Port}.", title, toEmail, host, port);
            }
            catch (Exception ex) when (ex is not EmailHeldException and not EmailSendingOffException)
            {
                _logger.LogError(ex, "[EMAIL] Failed to send announcement email '{Title}' to {ToEmail}.", title, toEmail);
                throw;
            }
        }

        public async Task SendHtmlReportAsync(string toEmail, string subject, string html)
        {
            var (fromEmail, password, host, port) = GetEmailSettings();

            // Throws when it cannot send (like the agreement reminder), so the weekly report job records a failure
            // and the "send now" button can tell the user it did not go out.
            if (string.IsNullOrWhiteSpace(fromEmail) ||
                string.IsNullOrWhiteSpace(toEmail) ||
                string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("[EMAIL] Missing email credentials or recipient email. Report email '{Subject}' not sent.", subject);
                throw new InvalidOperationException("Email credentials or recipient email missing; report not sent.");
            }

            try
            {
                using var mail = new MailMessage();
                mail.From = new MailAddress(fromEmail, "WorkNest Reports");
                mail.To.Add(new MailAddress(toEmail));
                mail.Subject = subject;
                mail.IsBodyHtml = true;
                mail.BodyEncoding = Encoding.UTF8;
                mail.SubjectEncoding = Encoding.UTF8;
                mail.Body = html;

                using var smtp = CreateSmtpClient(fromEmail, password, host, port);
                await DeliverAsync(smtp, mail, "Report e-mail");   // Email Sending off: held in Pending emails
                _logger.LogInformation("[EMAIL] Report '{Subject}' sent to {ToEmail} via {Host}:{Port}.", subject, toEmail, host, port);
            }
            catch (Exception ex) when (ex is not EmailHeldException and not EmailSendingOffException)
            {
                _logger.LogError(ex, "[EMAIL] Failed to send report '{Subject}' to {ToEmail}.", subject, toEmail);
                throw;
            }
        }
    }
}