using WorkNest.Application.DTOs.AuditLog;

namespace WorkNest.Application.Interfaces
{
    public interface IAuditLogRepository
    {
        Task<AuditLogPagedResultDto> GetPagedAuditLogsAsync(AuditLogFilterDto filter, CancellationToken ct = default);
        Task<AuditLogDetailDto?> GetAuditLogDetailAsync(long auditId, CancellationToken ct = default);
        Task<AuditLogFilterOptionsDto> GetFilterOptionsAsync(CancellationToken ct = default);
    }
}
