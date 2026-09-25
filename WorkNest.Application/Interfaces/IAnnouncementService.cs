using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Announcement;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IAnnouncementService
    {
        Task<ApiResponse> CreateAnnouncementAsync(CreateAnnouncementRequest request, int createdById);
        Task<ApiResponse> GetAnnouncementsAsync(int page = 1, int limit = 20, string? search = null);
        Task<ApiResponse> GetAnnouncementByIdAsync(Guid id);
        Task<ApiResponse> GetUserAnnouncementsAsync(int userId, int page = 1, int limit = 20);
        Task<ApiResponse> MarkAnnouncementReadAsync(Guid announcementId, int userId);
        Task<ApiResponse> RegisterDeviceTokenAsync(int userId, DeviceTokenRequest request);
    }
}
