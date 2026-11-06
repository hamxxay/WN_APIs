using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.NoticePeriod;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface INoticePeriodService
    {
        // Mobile customer endpoints
        Task<ApiResponse> SubmitNoticeAsync(int bookingId, int userId, string? note);
        Task<ApiResponse> WithdrawNoticeAsync(int noticeId, int userId);
        Task<ApiResponse> GetCustomerNoticesAsync(int userId);

        // Staff endpoints
        Task<ApiResponse> GetStaffNoticesAsync(int? locationId, string? status, string? allowedLocationIds);
        Task<ApiResponse> IssueNoticeAsync(StaffIssueNoticeRequest request, int staffUserId);
        Task<ApiResponse> DeclineNoticeAsync(int noticeId, string reason, int staffUserId);
        Task<ApiResponse> UpdateNoticeDateAsync(int noticeId, StaffUpdateNoticeDateRequest request, int staffUserId);
        Task<ApiResponse> GetSettlementPreCheckAsync(int noticeId);
        Task<ApiResponse> SaveSettlementAsync(SaveNoticeSettlementRequest request, int staffUserId);
    }
}

