namespace WorkNest.Application.Interfaces
{
    public interface ILoginHistoryService
    {
        Task Record(string eventType, int? userId, string? email, string? failureReason);
    }
}
