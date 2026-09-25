using System;
using System.Collections.Generic;

namespace WorkNest.Application.DTOs.Announcement
{
    public class CreateAnnouncementRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Type { get; set; } = "Announcement"; // 'Announcement' | 'Alert'
        public string TargetScope { get; set; } = "All";    // 'All' | 'Location' | 'Space' | 'CustomList'
        public int? LocationId { get; set; }
        public int? SpaceId { get; set; }
        public List<int>? CustomUserIds { get; set; }
        public DateTime? ScheduledAt { get; set; }
    }

    public class AnnouncementSummaryDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string TargetScope { get; set; } = string.Empty;
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        public int? SpaceId { get; set; }
        public string? SpaceName { get; set; }
        public DateTime? ScheduledAt { get; set; }
        public DateTime? SentAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public int CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalRecipients { get; set; }
        public int TotalUsers { get; set; }
        public int SentCount { get; set; }
        public int FailedCount { get; set; }
        public int ReadCount { get; set; }
        public int PendingCount { get; set; }
        public int PushSentCount { get; set; }
        public int EmailSentCount { get; set; }
    }

    public class AnnouncementDetailDto : AnnouncementSummaryDto
    {
        public List<AnnouncementRecipientDto> Recipients { get; set; } = new();
    }

    public class AnnouncementRecipientDto
    {
        public long Id { get; set; }
        public Guid AnnouncementId { get; set; }
        public int UserId { get; set; }
        public string? UserName { get; set; }
        public string? UserEmail { get; set; }
        public string Channel { get; set; } = string.Empty; // 'Push' | 'Email'
        public string Status { get; set; } = string.Empty;  // 'Pending' | 'Sent' | 'Failed' | 'Read'
        public int RetryCount { get; set; }
        public DateTime? SentAt { get; set; }
    }

    public class UserAnnouncementDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string TargetScope { get; set; } = string.Empty;
        public DateTime? ScheduledAt { get; set; }
        public DateTime? SentAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
    }

    public class DeviceTokenRequest
    {
        public string Token { get; set; } = string.Empty;
        public string Platform { get; set; } = "Android"; // 'iOS' | 'Android'
    }

    public class PendingDeliveryItemDto
    {
        public long RecipientId { get; set; }
        public Guid AnnouncementId { get; set; }
        public int UserId { get; set; }
        public string Channel { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? UserEmail { get; set; }
        public string? UserName { get; set; }
    }
}
