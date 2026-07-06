namespace WorkNest.Application.Interfaces
{
    /// <summary>Email notification dispatch.</summary>
    public interface IEmailService
    {
        Task SendTourNotificationAsync(string fullName, string email, string phone, string message);
    }
}
