using WorkNest.Application.DTOs.ServiceSwitch;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// On/off switch of each background service (<see cref="Services.BackgroundServiceCatalog"/>), kept in
    /// dbo.WN_ServiceSwitches (script SS_01_service_switches) and set by a super admin on the CMS. Off pauses the service's
    /// work without stopping the process; on resumes it, with no restart. Registered as a singleton (the hosted services
    /// that ask it are singletons).
    /// </summary>
    public interface IServiceSwitch
    {
        /// <summary>
        /// Whether the service may do its work now (cached for 10 seconds). Fails open: true when the service has no row,
        /// before SS_01 has run, or when the database cannot be read, so billing never stops silently.
        /// </summary>
        Task<bool> IsEnabledAsync(string name, CancellationToken ct = default);

        /// <summary>
        /// Turns the service on or off; <paramref name="user"/> (the signed-in e-mail) is stamped on the row and recorded
        /// in dbo.WN_AuditLog. Null for an unknown name. Throws <see cref="ServiceSwitchesMissingException"/> before SS_01.
        /// </summary>
        Task<ServiceSwitchState?> SetAsync(string name, bool enabled, string? user, CancellationToken ct = default);

        /// <summary>
        /// Every catalogue service with its switch, its appsettings flag on this server and whether it runs (also those
        /// without a row). Throws <see cref="ServiceSwitchesMissingException"/> before SS_01.
        /// </summary>
        Task<IReadOnlyList<ServiceSwitchState>> ListAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// dbo.WN_ServiceSwitches_GetAll / dbo.WN_ServiceSwitches_Set (script SS_01_service_switches). Before SS_01 has run
    /// (SQL errors 208 / 2812) both throw <see cref="ServiceSwitchesMissingException"/>.
    /// </summary>
    public interface IServiceSwitchRepository
    {
        /// <summary>Every row, ordered by name.</summary>
        Task<IReadOnlyList<ServiceSwitchRow>> GetAllAsync(CancellationToken ct = default);

        /// <summary>
        /// Sets the switch (the row changes only when the value changes) and returns the row; null when there is no row
        /// with that name. <paramref name="updatedBy"/> is also the audit e-mail (SESSION_CONTEXT, like DbRepository).
        /// </summary>
        Task<ServiceSwitchRow?> SetAsync(string serviceName, bool isEnabled, string? updatedBy, CancellationToken ct = default);
    }

    /// <summary>The database update SS_01_service_switches has not run: no table / procedures.</summary>
    public sealed class ServiceSwitchesMissingException : Exception
    {
        /// <summary>
        /// What the CMS shows (503). No "sql" in it: the CMS hides messages containing that word.
        /// </summary>
        public const string UserMessage =
            "Database update needed: run the script SS_01_service_switches_TEST (SS_01_service_switches_LIVE on the live database).";

        public ServiceSwitchesMissingException(Exception inner) : base(UserMessage, inner) { }
    }
}
