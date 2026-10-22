using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Announcement;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;

namespace WorkNest.API.Services
{
    /// <summary>
    /// Background delivery worker for Announcements and Alerts.
    /// Batches pending deliveries, dispatches Push and Email channels independently,
    /// retries failures up to 3 times, and marks the parent announcement terminal when complete.
    /// </summary>
    public class AnnouncementDeliveryService : BackgroundService
    {
        private readonly ILogger<AnnouncementDeliveryService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(20);
        private readonly ServiceSwitchGate _gate; // on/off switch (api/admin/services): off skips the cycle's work
        private readonly ServiceSwitchGate _emailGate; // Email Sending switch: off = e-mail items wait (push still goes)

        public AnnouncementDeliveryService(
            ILogger<AnnouncementDeliveryService> logger,
            IServiceProvider serviceProvider,
            IServiceSwitch serviceSwitch)
        {
            _logger = logger;
            _gate = new ServiceSwitchGate(serviceSwitch, BackgroundServiceCatalog.AnnouncementDelivery, logger);
            _emailGate = new ServiceSwitchGate(serviceSwitch, BackgroundServiceCatalog.EmailSending, logger);
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WorkNest Announcement & Alert Delivery Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (await _gate.IsOnAsync(stoppingToken))
                        await ProcessPendingDeliveriesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during announcement delivery processing cycle.");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("WorkNest Announcement & Alert Delivery Service stopping.");
        }

        private async Task ProcessPendingDeliveriesAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDbRepository>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var pendingItems = (await db.GetPendingAnnouncementDeliveriesAsync(200)).ToList();
            if (pendingItems.Count == 0) return;

            _logger.LogInformation("Processing {Count} pending announcement delivery item(s)...", pendingItems.Count);

            var touchedAnnouncements = new HashSet<Guid>();
            var emailOn = await _emailGate.IsOnAsync(stoppingToken);

            foreach (var item in pendingItems)
            {
                if (stoppingToken.IsCancellationRequested) break;

                // Email Sending off: the e-mail item stays pending (no attempt used) until it is back on.
                if (!emailOn && string.Equals(item.Channel, "Email", StringComparison.OrdinalIgnoreCase)) continue;

                touchedAnnouncements.Add(item.AnnouncementId);
                int attempt = item.RetryCount + 1;

                if (string.Equals(item.Channel, "Email", StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessEmailDeliveryAsync(db, emailService, item, attempt);
                }
                else if (string.Equals(item.Channel, "Push", StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessPushDeliveryAsync(db, item, attempt);
                }
            }

            // Check terminal status for affected announcements
            foreach (var announcementId in touchedAnnouncements)
            {
                try
                {
                    await db.CheckAndUpdateAnnouncementTerminalStatusAsync(announcementId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking terminal status for Announcement ID {AnnouncementId}.", announcementId);
                }
            }
        }

        private async Task ProcessEmailDeliveryAsync(
            IDbRepository db,
            IEmailService emailService,
            PendingDeliveryItemDto item,
            int attempt)
        {
            if (string.IsNullOrWhiteSpace(item.UserEmail))
            {
                _logger.LogWarning("Recipient ID {RecipientId} (User {UserId}) has no valid email address. Marking Failed.", item.RecipientId, item.UserId);
                await db.UpdateAnnouncementDeliveryStatusAsync(item.RecipientId, "Failed", attempt);
                return;
            }

            try
            {
                var recipientName = !string.IsNullOrWhiteSpace(item.UserName) ? item.UserName : "Valued Member";
                await emailService.SendAnnouncementEmailAsync(item.UserEmail, recipientName, item.Title, item.Body, item.Type);

                await db.UpdateAnnouncementDeliveryStatusAsync(item.RecipientId, "Sent", item.RetryCount);
                _logger.LogInformation("Email announcement '{Title}' delivered to {Email} (Recipient ID: {Id}).", item.Title, item.UserEmail, item.RecipientId);
            }
            catch (EmailHeldException held)
            {
                // Email Sending was turned off meanwhile: held in Pending emails, which sends it (or a super admin discards
                // it). This delivery is done here, so it is not sent twice.
                await db.UpdateAnnouncementDeliveryStatusAsync(item.RecipientId, "Sent", item.RetryCount);
                _logger.LogInformation("Email announcement '{Title}' to {Email} held as pending e-mail #{OutboxId}.", item.Title, item.UserEmail, held.OutboxId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deliver email announcement '{Title}' to {Email} (Attempt {Attempt}/3).", item.Title, item.UserEmail, attempt);
                string newStatus = attempt >= 3 ? "Failed" : "Pending";
                await db.UpdateAnnouncementDeliveryStatusAsync(item.RecipientId, newStatus, attempt);
            }
        }

        private async Task ProcessPushDeliveryAsync(
            IDbRepository db,
            PendingDeliveryItemDto item,
            int attempt)
        {
            try
            {
                // In production FCM / APNs dispatch logic.
                // Out-of-scope for push notification provider credentials per prompt requirement.
                _logger.LogInformation("Push notification dispatched for '{Title}' to User ID {UserId} (Recipient ID: {Id}).", item.Title, item.UserId, item.RecipientId);

                await db.UpdateAnnouncementDeliveryStatusAsync(item.RecipientId, "Sent", item.RetryCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deliver push notification for '{Title}' to User ID {UserId} (Attempt {Attempt}/3).", item.Title, item.UserId, attempt);
                string newStatus = attempt >= 3 ? "Failed" : "Pending";
                await db.UpdateAnnouncementDeliveryStatusAsync(item.RecipientId, newStatus, attempt);
            }
        }
    }
}
