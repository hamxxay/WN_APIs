using WorkNest.Application.DTOs.LoginHistory;

namespace WorkNest.Application.Interfaces
{
    public interface ILoginHistoryRepository
    {
        Task<LoginHistoryPagedResultDto> GetPagedLoginHistoryAsync(LoginHistoryFilterDto filter, CancellationToken ct = default);
        Task<LoginHistoryStatsDto> GetStatsAsync(CancellationToken ct = default);
    }
}
