using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.NoticePeriod;

namespace WorkNest.Application.Interfaces
{
    public interface INoticePeriodRepository
    {
        Task<NoticePeriodItemDto?> SubmitNoticeDbAsync(int bookingId, int userId, string source, string? note);
        Task<NoticePeriodItemDto?> WithdrawNoticeDbAsync(int noticeId, int userId);
        Task<List<NoticePeriodItemDto>> GetCustomerNoticesDbAsync(int userId);
        Task<List<NoticePeriodItemDto>> GetStaffNoticesDbAsync(int? locationId, string? status, string? allowedLocationIds);
        Task<NoticePeriodItemDto?> IssueNoticeDbAsync(int? noticeId, int? bookingId, DateTime noticeDate, int? noticeDays, string? staffNote, int issuedById);
        Task<NoticePeriodItemDto?> DeclineNoticeDbAsync(int noticeId, string reason, int declinedById);
        Task<NoticePeriodItemDto?> UpdateNoticeDateDbAsync(int noticeId, DateTime noticeDate, string? staffNote, int updatedById);
        Task<SettlementPreCheckDto?> GetSettlementPreCheckDbAsync(int noticeId);
        Task<NoticeSettlementResultDto?> SaveSettlementDbAsync(SaveNoticeSettlementRequest request, int settledById);
    }
}

