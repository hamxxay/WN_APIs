using System;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Announcement;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class AnnouncementService : IAnnouncementService
    {
        private readonly IDbRepository _db;

        public AnnouncementService(IDbRepository db)
        {
            _db = db;
        }

        public async Task<ApiResponse> CreateAnnouncementAsync(CreateAnnouncementRequest request, int createdById)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return ApiResponse.Fail("Announcement title is required.");

            if (string.IsNullOrWhiteSpace(request.Body))
                return ApiResponse.Fail("Announcement message body is required.");

            var validTypes = new[] { "Announcement", "Alert" };
            if (Array.IndexOf(validTypes, request.Type) < 0)
                request.Type = "Announcement";

            var validScopes = new[] { "All", "Location", "Space", "CustomList" };
            if (Array.IndexOf(validScopes, request.TargetScope) < 0)
                request.TargetScope = "All";

            if (request.TargetScope == "Location" && (!request.LocationId.HasValue || request.LocationId.Value <= 0))
                return ApiResponse.Fail("A valid LocationId is required when TargetScope is 'Location'.");

            if (request.TargetScope == "Space" && (!request.SpaceId.HasValue || request.SpaceId.Value <= 0))
                return ApiResponse.Fail("A valid SpaceId is required when TargetScope is 'Space'.");

            if (request.TargetScope == "CustomList" && (request.CustomUserIds == null || request.CustomUserIds.Count == 0))
                return ApiResponse.Fail("At least one recipient UserId is required when TargetScope is 'CustomList'.");

            var result = await _db.CreateAnnouncementAsync(
                request.Title.Trim(),
                request.Body.Trim(),
                request.Type,
                request.TargetScope,
                request.LocationId,
                request.SpaceId,
                request.CustomUserIds,
                request.ScheduledAt,
                createdById
            );

            if (result == null)
                return ApiResponse.Fail("Failed to create and queue announcement.");

            return ApiResponse.Ok(result, "Announcement created and recipients queued successfully.");
        }

        public async Task<ApiResponse> GetAnnouncementsAsync(int page = 1, int limit = 20, string? search = null)
        {
            if (page < 1) page = 1;
            if (limit < 1 || limit > 100) limit = 20;

            var (items, total) = await _db.GetAnnouncementsAsync(page, limit, search);
            return ApiResponse.Ok(new { items, total, page, limit });
        }

        public async Task<ApiResponse> GetAnnouncementByIdAsync(Guid id)
        {
            var result = await _db.GetAnnouncementByIdAsync(id);
            if (result == null)
                return ApiResponse.Fail("Announcement not found.");

            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetUserAnnouncementsAsync(int userId, int page = 1, int limit = 20)
        {
            if (page < 1) page = 1;
            if (limit < 1 || limit > 100) limit = 20;

            var (items, total) = await _db.GetUserAnnouncementsAsync(userId, page, limit);
            return ApiResponse.Ok(new { items, total, page, limit });
        }

        public async Task<ApiResponse> MarkAnnouncementReadAsync(Guid announcementId, int userId)
        {
            var success = await _db.MarkAnnouncementReadAsync(announcementId, userId);
            if (!success)
                return ApiResponse.Fail("Announcement not found or already read.");

            return ApiResponse.Ok(new { announcementId, userId, isRead = true }, "Marked as read.");
        }

        public async Task<ApiResponse> RegisterDeviceTokenAsync(int userId, DeviceTokenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return ApiResponse.Fail("Device token is required.");

            var platform = string.Equals(request.Platform, "iOS", StringComparison.OrdinalIgnoreCase) ? "iOS" : "Android";
            await _db.RegisterDeviceTokenAsync(userId, request.Token.Trim(), platform);

            return ApiResponse.Ok(new { userId, platform, registered = true }, "Device token registered successfully.");
        }
    }
}
