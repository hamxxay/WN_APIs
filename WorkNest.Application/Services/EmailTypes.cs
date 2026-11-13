using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>The two sending mailboxes. Passwords live only in server configuration.</summary>
    public static class EmailMailboxes
    {
        public const string Sales = "sales";
        public const string Notifications = "notifications";

        public static bool IsValid(string? mailbox) => mailbox == Sales || mailbox == Notifications;

        /// <summary>The settings of the notifications mailbox (Email:Notifications:*), or null when its address or password is missing.
        /// Host / port default to the main mailbox's.</summary>
        public static (string FromEmail, string Password, string Host, int Port)? NotificationsSettings(IConfiguration config)
        {
            var from = config["Email:Notifications:FromEmail"];
            var pass = config["Email:Notifications:Password"];
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(pass)) return null;
            var host = config["Email:Notifications:SmtpHost"];
            if (string.IsNullOrWhiteSpace(host)) host = config["Email:SmtpHost"] ?? "smtppro.zoho.com";
            var port = int.TryParse(config["Email:Notifications:SmtpPort"], out var np) ? np
                     : int.TryParse(config["Email:SmtpPort"], out var p) ? p : 587;
            return (from.Trim(), pass, host, port);
        }
    }

    /// <summary>One e-mail the system sends: its key, who receives it and the mailbox it uses unless a super admin changed it.</summary>
    public sealed record EmailTypeInfo(string Key, string Label, string Recipient, string Group, string DefaultMailbox,
        bool Approval = false, bool BypassHold = false);

    /// <summary>Every e-mail type (CMS Administration > Email Senders). Keys are stored in dbo.WN_EmailSenders.</summary>
    public static class EmailTypes
    {
        public const string GroupCustomer = "customer";
        public const string GroupNotifications = "notifications";

        public const string Quotation = "quotation";
        public const string Agreement = "agreement";
        public const string AgreementReminder = "agreement_reminder";
        public const string Invoice = "invoice";
        public const string BookingConfirmation = "booking_confirmation";
        public const string SurchargeInvoice = "surcharge_invoice";
        public const string CustomerLogin = "customer_login";
        public const string NoticeConfirmed = "notice_confirmed";
        public const string NoticeDeclined = "notice_declined";
        public const string TourRequest = "tour_request";
        public const string NoticeRequest = "notice_request";
        public const string PendingInvoices = "pending_invoices";
        public const string QuotationApproval = "quotation_approval";
        public const string ApprovalRequest = "approval_request";
        public const string ApprovalDecision = "approval_decision";
        public const string NetworkAlert = "network_alert";
        public const string WeeklyReport = "weekly_report";
        public const string Announcement = "announcement";

        public static readonly IReadOnlyList<EmailTypeInfo> All = new List<EmailTypeInfo>
        {
            new(Quotation, "Quotation (PDF)", "Customer", GroupCustomer, EmailMailboxes.Sales),
            new(Agreement, "Agreement to sign (PDF), including space transfers", "Customer", GroupCustomer, EmailMailboxes.Sales),
            new(AgreementReminder, "Agreement reminder", "Customer", GroupCustomer, EmailMailboxes.Sales),
            new(Invoice, "Challan / invoice (PDF), including recurring and delivery retries", "Customer", GroupCustomer, EmailMailboxes.Sales),
            new(BookingConfirmation, "Booking confirmation", "Customer", GroupCustomer, EmailMailboxes.Sales),
            new(SurchargeInvoice, "Extra-person surcharge invoice", "Customer", GroupCustomer, EmailMailboxes.Sales),
            new(CustomerLogin, "Your WorkNest login (temporary password)", "Customer", GroupCustomer, EmailMailboxes.Sales),
            new(NoticeConfirmed, "Notice period confirmed", "Customer", GroupCustomer, EmailMailboxes.Sales),
            new(NoticeDeclined, "Notice period declined", "Customer", GroupCustomer, EmailMailboxes.Sales),
            new(TourRequest, "New tour request", "Sales team", GroupNotifications, EmailMailboxes.Notifications),
            new(NoticeRequest, "New notice-to-leave request", "admin@worknestpk.com", GroupNotifications, EmailMailboxes.Notifications),
            new(PendingInvoices, "Pending invoice approvals reminder", "Location staff", GroupNotifications, EmailMailboxes.Notifications),
            new(QuotationApproval, "Quotation waiting / approved / sent back / 24 h reminder", "Sales manager / executive", GroupNotifications, EmailMailboxes.Notifications, Approval: true),
            new(ApprovalRequest, "Networking / CCTV approver link", "Approver", GroupNotifications, EmailMailboxes.Notifications, Approval: true),
            new(ApprovalDecision, "Networking / CCTV decision", "Requester", GroupNotifications, EmailMailboxes.Notifications, Approval: true),
            new(NetworkAlert, "Network down / up alerts", "Alert recipients", GroupNotifications, EmailMailboxes.Notifications, BypassHold: true),
            new(WeeklyReport, "Weekly report", "Report recipients", GroupNotifications, EmailMailboxes.Notifications),
            new(Announcement, "Announcements", "Customers", GroupNotifications, EmailMailboxes.Notifications),
        };

        public static EmailTypeInfo? Find(string? key) => All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>One dbo.WN_EmailSenders row.</summary>
    public sealed record EmailSenderRow(string TypeKey, string Mailbox, string? FromName, string? UpdatedBy, DateTime? UpdatedAt);

    /// <summary>Pure rules of the Email Senders page.</summary>
    public static class EmailSenderRules
    {
        public const int FromNameMax = 100;

        /// <summary>Plain-English check of a save request. Null = fine.</summary>
        public static string? Validate(string? mailbox, string? fromName)
        {
            if (!EmailMailboxes.IsValid(mailbox)) return "Mailbox must be 'sales' or 'notifications'.";
            if (fromName != null && fromName.Trim().Length > FromNameMax) return $"The sender name can be at most {FromNameMax} characters.";
            return null;
        }

        /// <summary>The mailbox and sender name of a type: the saved row, else the default. Unknown type: sales.</summary>
        public static (string Mailbox, string? FromName) Resolve(string typeKey, IReadOnlyDictionary<string, EmailSenderRow>? rows)
        {
            if (rows != null && rows.TryGetValue(typeKey, out var r) && EmailMailboxes.IsValid(r.Mailbox))
                return (r.Mailbox, string.IsNullOrWhiteSpace(r.FromName) ? null : r.FromName.Trim());
            return (EmailTypes.Find(typeKey)?.DefaultMailbox ?? EmailMailboxes.Sales, null);
        }

        public sealed record Item(string TypeKey, string Label, string Recipient, string Group, string Mailbox, string? FromName,
            string DefaultMailbox, string? UpdatedBy, DateTime? UpdatedAt);

        public static Item ToItem(EmailTypeInfo t, IReadOnlyDictionary<string, EmailSenderRow>? rows)
        {
            var (mailbox, name) = Resolve(t.Key, rows);
            EmailSenderRow? row = null;
            rows?.TryGetValue(t.Key, out row);
            return new Item(t.Key, t.Label, t.Recipient, t.Group, mailbox, name, t.DefaultMailbox, row?.UpdatedBy, row?.UpdatedAt);
        }

        public static IReadOnlyList<Item> Items(IReadOnlyDictionary<string, EmailSenderRow>? rows) => EmailTypes.All.Select(t => ToItem(t, rows)).ToList();
    }
}
