using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Main admin dashboard data (database update DASH_01_main_dashboard): the read-only procedures
    /// dbo.WN_Dash_SpaceTypeSummary, dbo.WN_Dash_SpaceTypeDetails and dbo.WN_Dash_SpaceInventory. Location lists are
    /// sent as a comma-separated list (null = every location; an empty list matches nothing). Before DASH_01 has run
    /// every call throws <see cref="MainDashboardDatabaseUpdateMissingException"/>.
    /// </summary>
    public interface IMainDashboardRepository
    {
        /// <summary>[0] one row per space type card.</summary>
        Task<List<List<IDictionary<string, object?>>>> GetSpaceTypeSummaryAsync(IReadOnlyCollection<int>? locationIds, int expiringDays,
            DateTime businessNow);

        /// <summary>[0] Total, [1] the rows of the page.</summary>
        Task<List<List<IDictionary<string, object?>>>> GetSpaceTypeDetailsAsync(IReadOnlyCollection<int>? locationIds, int spaceTypeId,
            string tag, int expiringDays, int page, int pageSize, DateTime businessNow);

        /// <summary>[0] Total, [1] the rows of the page.</summary>
        Task<List<List<IDictionary<string, object?>>>> GetInventoryAsync(IReadOnlyCollection<int>? locationIds, int? locationId,
            string? search, string? customer, string? spaceType, string? status, bool privateOnly, int expiringDays, string sortBy,
            bool sortDesc, int page, int pageSize, DateTime businessNow);
    }

    /// <summary>
    /// The main dashboard's space type cards, tag lists and inventory table (api/dashboard/main-summary,
    /// api/dashboard/main-summary/details, api/dashboard/inventory). Bad parameters: a failed ApiResponse (400).
    /// </summary>
    public interface IMainDashboardService
    {
        Task<ApiResponse> GetSummaryAsync(IReadOnlyCollection<int>? locationIds, int? expiringDays);
        Task<ApiResponse> GetDetailsAsync(IReadOnlyCollection<int>? locationIds, int spaceTypeId, string? tag, int? expiringDays, int page, int pageSize);
        Task<ApiResponse> GetInventoryAsync(IReadOnlyCollection<int>? locationIds, MainDashboardInventoryQuery query);
    }

    /// <summary>
    /// The procedures of DASH_01_main_dashboard are not on the database yet (SQL error 2812 / 208). The API answers 503
    /// with <see cref="UserMessage"/>, which the page shows in the sections that need them.
    /// </summary>
    public sealed class MainDashboardDatabaseUpdateMissingException : Exception
    {
        // No ".sql" in the text: the CMS and the app hide messages that mention SQL.
        public const string UserMessage =
            "Database update needed: run the script DASH_01_main_dashboard_TEST (on the live database DASH_01_main_dashboard_LIVE).";

        public MainDashboardDatabaseUpdateMissingException(Exception inner) : base(UserMessage, inner)
        {
        }
    }
}
