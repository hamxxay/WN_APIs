using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public sealed record TempPasswordIssued(int CustomerId, string Email, string TempPassword, bool Emailed);

    public interface ICustomerLoginService
    {
        /// <summary>
        /// Gives the customer a new temporary password (creates the login user when missing) and e-mails it. Status code:
        /// 200, 400 (no email / not a customer account / turned off), 404, 503 (AUTH_TEMP_01 not run).
        /// </summary>
        /// <summary>True when the customer has a login (temporary or active) but belongs to no location yet.</summary>
        Task<bool> HasLoginWithoutLocationAsync(int customerId, CancellationToken ct = default);

        Task<(int StatusCode, ApiResponse Response)> IssueAsync(int customerId, CancellationToken ct = default);

        /// <summary>Same for every customer with an email whose login is not active. Never touches an active login.</summary>
        Task<(int StatusCode, ApiResponse Response)> IssueBulkAsync(CancellationToken ct = default);

        /// <summary>Used by customer create: null when it could not be done (the reason in <paramref name="error"/>).</summary>
        Task<(TempPasswordIssued? Login, string? Error)> TryIssueAsync(int customerId, CancellationToken ct = default);

        /// <summary>Adds loginStatus, canUseGoogle and tempPassword (admin callers, pending only) to customer rows.</summary>
        Task EnrichAsync(IEnumerable<IDictionary<string, object?>> rows, bool includeTempPassword, CancellationToken ct = default);
    }
}
