using WorkNest.Application.DTOs.Dashboard;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Raw result sets for the role-based dashboard sections (see RoleDashboardRepository).</summary>
    public interface IRoleDashboardRepository
    {
        // periodEnd: a custom range (DASH_01), the day after its last day; null = to date (month / quarter / year, as before).
        Task<List<List<IDictionary<string, object?>>>> GetSalesAsync(IReadOnlyCollection<int>? locationIds, DateTime periodStart, DateTime now, IEnumerable<int> openInvoiceStatusIds, int renewalDays,
            DateTime? periodEnd = null);
        Task<List<List<IDictionary<string, object?>>>> GetTeamAsync(IReadOnlyCollection<int>? locationIds, DateTime now);
        Task<List<List<IDictionary<string, object?>>>> GetLocationComparisonAsync(DateTime periodStart, DateTime now, IEnumerable<int> openInvoiceStatusIds, IEnumerable<int> voidStatusIds,
            DateTime? periodEnd = null);
        Task<List<List<IDictionary<string, object?>>>> GetStaffAsync(DateTime periodStart, DateTime? periodEnd = null);
        Task<List<List<IDictionary<string, object?>>>> GetSystemAsync();
    }

    /// <summary>
    /// Dashboard sections that differ by role: sales executive = sales pipeline, inquiries, renewals, collections;
    /// admin = team &amp; service (on top of the branch overview); super admin = team &amp; service, location comparison,
    /// staff and system health.
    /// </summary>
    public interface IRoleDashboardService
    {
        /// <param name="period">month | quarter | year (to date) | custom (<paramref name="from"/> .. <paramref name="to"/>, yyyy-MM-dd).</param>
        Task<RoleDashboardDto> GetAsync(string role, IReadOnlyCollection<int>? locationIds, string? period, string? from = null, string? to = null);
    }
}
