using WorkNest.Application.DTOs.Reports;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Raw data for the forecast and the weekly report (see ReportRepository), and the weekly run log.</summary>
    public interface IReportRepository
    {
        /// <summary>One row per location and forecast month (location 0 = spaces without a location).</summary>
        Task<List<List<IDictionary<string, object?>>>> GetForecastAsync(IReadOnlyCollection<int>? locationIds, DateTime firstMonth, int months);
        /// <summary>[0] tour inquiries since weekStart; [1] leases expiring in 30 / 60 days per location; [2] the first 10 expiring.</summary>
        Task<List<List<IDictionary<string, object?>>>> GetWeeklyExtrasAsync(DateTime weekStart, DateTime now);
        /// <summary>E-mail addresses of active super admins.</summary>
        Task<List<string>> GetSuperAdminEmailsAsync();
        /// <summary>Claims (reportKey, periodKey) in WN_ScheduledReportRuns; a failed run older than retryFailedBefore can be claimed again.</summary>
        Task<ReportClaimResult> TryClaimRunAsync(string reportKey, string periodKey, DateTime at, DateTime retryFailedBefore);
        /// <summary>Records how a claimed run ended (Sent / Partial / Failed).</summary>
        Task CompleteRunAsync(string reportKey, string periodKey, string status, string recipients, string? error, DateTime at);
    }

    /// <summary>3-month (up to 12) revenue &amp; occupancy forecast from current leases.</summary>
    public interface IForecastService
    {
        /// <summary>locationIds null = all locations.</summary>
        Task<ForecastDto> GetAsync(IReadOnlyCollection<int>? locationIds, int months = 3);
    }

    /// <summary>Builds and sends the weekly super admin report.</summary>
    public interface IWeeklyReportGenerator
    {
        /// <summary>Numbers for the last 7 days / now, the forecast, and the e-mail HTML.</summary>
        Task<WeeklyReportDto> BuildAsync();
        /// <summary>Active super admins plus the extra addresses, valid and without repeats.</summary>
        Task<List<string>> GetRecipientsAsync(IEnumerable<string>? extraRecipients);
        /// <summary>E-mails the report to each recipient separately (one bad address does not stop the rest).</summary>
        Task<WeeklyReportSendResultDto> SendAsync(WeeklyReportDto report, IReadOnlyCollection<string> recipients, CancellationToken ct = default);
    }
}
