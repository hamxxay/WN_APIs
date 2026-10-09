using WorkNest.Application.DTOs.Dashboard;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Raw result sets for the role-based dashboard sections (see RoleDashboardRepository).</summary>
    public interface IRoleDashboardRepository
    {
        Task<List<List<IDictionary<string, object?>>>> GetSalesAsync(IReadOnlyCollection<int>? locationIds, DateTime periodStart, DateTime now, IEnumerable<int> openInvoiceStatusIds, int renewalDays);
        Task<List<List<IDictionary<string, object?>>>> GetTeamAsync(IReadOnlyCollection<int>? locationIds, DateTime now);
        Task<List<List<IDictionary<string, object?>>>> GetLocationComparisonAsync(DateTime periodStart, DateTime now, IEnumerable<int> openInvoiceStatusIds, IEnumerable<int> voidStatusIds);
        Task<List<List<IDictionary<string, object?>>>> GetStaffAsync(DateTime periodStart);
        Task<List<List<IDictionary<string, object?>>>> GetSystemAsync();
    }

    /// <summary>
    /// Dashboard sections that differ by role: sales executive = sales pipeline, inquiries, renewals, collections;
    /// admin = team &amp; service (on top of the branch overview); super admin = team &amp; service, location comparison,
    /// staff and system health.
    /// </summary>
    public interface IRoleDashboardService
    {
        Task<RoleDashboardDto> GetAsync(string role, IReadOnlyCollection<int>? locationIds, string? period);
    }
}
