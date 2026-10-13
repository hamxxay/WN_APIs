namespace WorkNest.Application.DTOs.LoginHistory
{
    public class LoginHistoryStatsDto
    {
        public int LoginsToday { get; set; }
        public int FailedToday { get; set; }
        public int DistinctUsersToday { get; set; }
    }

    public class LoginHistoryPagedResultDto
    {
        public List<LoginHistoryDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
        public LoginHistoryStatsDto Stats { get; set; } = new();
    }
}
