namespace WorkNest.Application.Interfaces
{
    public interface ILoginHistoryService
    {
        Task Record(string eventType, int? userId, string? email, string? failureReason);

        /// <summary>
        /// True when WN_LoginHistory has a successful sign-in (LOGIN) for this user: by UserId, or by email on a row
        /// written before the user row existed (UserId NULL). False when there is none or the table can't be read
        /// (the sign-in check then treats the account as never signed in).
        /// </summary>
        Task<bool> HasSignedInBeforeAsync(int userId, string? email);
    }
}
