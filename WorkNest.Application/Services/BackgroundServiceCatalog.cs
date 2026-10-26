using Microsoft.Extensions.Configuration;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// One background service of the API that has an on/off switch (dbo.WN_ServiceSwitches, script SS_01_service_switches).
    /// <see cref="ConfigKey"/> is the appsettings flag that can also turn it off on one server; the database switch never
    /// overrides it (a service switched off in appsettings stays off).
    /// </summary>
    public sealed class BackgroundServiceInfo
    {
        private readonly Func<IConfiguration, bool> _configEnabled;

        public BackgroundServiceInfo(string name, string displayName, string description, string? configKey, bool seedEnabled,
            Func<IConfiguration, bool> configEnabled)
        {
            Name = name;
            DisplayName = displayName;
            Description = description;
            ConfigKey = configKey;
            SeedEnabled = seedEnabled;
            _configEnabled = configEnabled;
        }

        /// <summary>The switch name: dbo.WN_ServiceSwitches.ServiceName and api/admin/services/{name}.</summary>
        public string Name { get; }
        public string DisplayName { get; }
        /// <summary>One line: what the service does and how often.</summary>
        public string Description { get; }
        /// <summary>The appsettings flag that turns it off on this server (null = none).</summary>
        public string? ConfigKey { get; }
        /// <summary>The starting value of the switch in SS_01.</summary>
        public bool SeedEnabled { get; }

        /// <summary>The appsettings flag read the way the service itself reads it (true when the service has no flag).</summary>
        public bool IsConfigEnabled(IConfiguration configuration) => _configEnabled(configuration);
    }

    /// <summary>
    /// The background services with an on/off switch: the one list the switches API, the hosted services and the seed rows
    /// of SS_01_service_switches follow. Keep the names in step with that script.
    /// </summary>
    public static class BackgroundServiceCatalog
    {
        public const string EmailSending = "EmailSending";
        public const string BillingAutomation = "BillingAutomation";
        public const string InvoiceDeliveryRetry = "InvoiceDeliveryRetry";
        public const string AccessCardRestriction = "AccessCardRestriction";
        public const string AnnouncementDelivery = "AnnouncementDelivery";
        public const string ChallanAccessSuspension = "ChallanAccessSuspension";
        public const string BookingAutoConfirm = "BookingAutoConfirm";
        public const string WeeklyReport = "WeeklyReport";
        public const string UnifiPolling = "UnifiPolling";
        public const string HikSync = "HikSync";
        public const string AgreementReminders = "AgreementReminders";
        public const string GwnPolling = "GwnPolling";
        public const string WhatsAppFollowUp = "WhatsAppFollowUp";

        /// <summary>The services in the order the CMS lists them.</summary>
        public static readonly IReadOnlyList<BackgroundServiceInfo> All = new[]
        {
            // Not a loop of its own: EmailService asks it before every e-mail (SS_02_email_sending adds its row).
            new BackgroundServiceInfo(EmailSending, "Email Sending",
                "Every e-mail the system sends: invoices, challans, agreements, quotations, booking confirmations, announcements, reports and reminders. Off: nothing is e-mailed; the e-mails asked for meanwhile wait in Pending emails and go out when it is on again, unless discarded.",
                null, true, _ => true),
            new BackgroundServiceInfo(BillingAutomation, "Billing Automation",
                "Every hour: creates the next recurring invoice of each active booking 5 days before its billing cycle ends (security deposit charged once).",
                null, true, _ => true),
            new BackgroundServiceInfo(InvoiceDeliveryRetry, "Invoice Delivery Retry",
                "Every 5 minutes: sends again the invoice e-mails that failed (up to 3 attempts).",
                null, true, _ => true),
            new BackgroundServiceInfo(AccessCardRestriction, "Access Card Restriction",
                "Once a day: restricts the access cards again of bookings whose partial-payment period has lapsed with a balance unpaid.",
                null, true, _ => true),
            new BackgroundServiceInfo(AnnouncementDelivery, "Announcement & Alert Delivery",
                "Every 20 seconds: sends pending announcements and alerts by push notification and e-mail (up to 3 retries).",
                null, true, _ => true),
            new BackgroundServiceInfo(ChallanAccessSuspension, "Challan Access Suspension",
                "Every hour: suspends door access on the HIK machines for bookings whose challan is past its due date and unpaid, until it is paid.",
                "HikAccessSuspension:Enabled", true, c => NotFalse(c["HikAccessSuspension:Enabled"])),
            new BackgroundServiceInfo(BookingAutoConfirm, "Booking Auto-Confirm",
                "Every 5 minutes: confirms Pending bookings whose first invoice has been paid.",
                "BookingAutoConfirm:Enabled", true, c => NotFalse(c["BookingAutoConfirm:Enabled"])),
            new BackgroundServiceInfo(WeeklyReport, "Weekly Report",
                "Every 15 minutes: e-mails the weekly report to the super admins once a week (Monday 09:00 Pakistan time by default).",
                "Reports:Weekly:Enabled", true, c => NotFalse(c["Reports:Weekly:Enabled"])),
            // Like UnifiPollingService: off when Unifi:Enabled is "false", and it does nothing without Unifi:ApiKey.
            new BackgroundServiceInfo(UnifiPolling, "UniFi Polling",
                "Keeps the UniFi network dashboard fresh: sites, devices and clients every minute, ISP metrics and the event log every 5 minutes (needs Unifi:ApiKey).",
                "Unifi:Enabled", true, c => NotFalse(c["Unifi:Enabled"]) && !string.IsNullOrWhiteSpace(c["Unifi:ApiKey"])),
            // HikSyncOptions.Enabled is bound from HikSync:Enabled: off unless it is true.
            new BackgroundServiceInfo(HikSync, "HIK Sync",
                "Keeps the HIK door machines and the database in step: online check every 2 minutes, maintenance every 5, enrolment watcher every 90 seconds, clock sync daily at 04:00.",
                "HikSync:Enabled", false, c => bool.TryParse(c["HikSync:Enabled"], out var on) && on),
            new BackgroundServiceInfo(AgreementReminders, "Agreement Reminder",
                "Every hour from 09:00 to 20:00 Pakistan time: e-mails customers whose agreement still waits for their signature (after 3 and 7 days).",
                "AgreementReminders:Enabled", false, c => NotFalse(c["AgreementReminders:Enabled"])),
            // Like GwnPollingService: off when Gwn:Enabled is "false", and it does nothing without the Gwn credentials.
            // Seed row in GWN_01_grandstream (on).
            new BackgroundServiceInfo(GwnPolling, "Grandstream Polling",
                "Keeps the Grandstream (GWN, F-7) network figures fresh: devices, clients and Wi-Fi every minute, statistics every 5 minutes (needs the Gwn settings).",
                "Gwn:Enabled", true, c => NotFalse(c["Gwn:Enabled"]) && !string.IsNullOrWhiteSpace(c["Gwn:AppId"])),
            // WhatsAppFollowUpService; seed row in Database/Contacts/INQ_01_simple_inquiries (on).
            new BackgroundServiceInfo(WhatsAppFollowUp, "WhatsApp Follow-Up",
                "Every 30 seconds: a WhatsApp customer who typed a message instead of choosing a bot menu option and then went quiet for 2 minutes gets \"Our sales representative will contact you shortly.\" and appears under Tour Inquiries > Simple Inquiries (at most once a day per customer).",
                "WhatsApp:FollowUpEnabled", true, c => NotFalse(c["WhatsApp:FollowUpEnabled"])),
        };

        /// <summary>The service with this name (case ignored); null when there is none.</summary>
        public static BackgroundServiceInfo? Find(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var key = name.Trim();
            return All.FirstOrDefault(s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The service with this name; throws for a name that is not in the list (a programming error).</summary>
        public static BackgroundServiceInfo Get(string name) =>
            Find(name) ?? throw new ArgumentException($"'{name}' is not a background service in BackgroundServiceCatalog.", nameof(name));

        /// <summary>The services' own reading of their flags: anything but "false" (any case) means on.</summary>
        private static bool NotFalse(string? value) => !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }
}
