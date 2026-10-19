namespace WorkNest.Application.Interfaces
{
    /// <summary>The overview chart drill-down (GET api/dashboard/overview-details/{kind}, script DASH_02).</summary>
    public static class DashboardOverviewDetails
    {
        /// <summary>
        /// Answer before the database update DASH_02 has run (its procedures are missing). No ".sql" in it: the CMS hides
        /// messages that mention SQL.
        /// </summary>
        public const string DbUpdateMessage =
            "Database update needed: run the script DASH_02_main_dashboard_drilldown_TEST (on LIVE: DASH_02_main_dashboard_drilldown_LIVE).";

        /// <summary>
        /// revenue-month (month): invoices issued that month. aging-bucket (bucket): open invoices of that bucket.
        /// top-customer (customer; period or from / to): that customer's invoices. new-bookings (month): bookings made that
        /// month. leases-ending (month): live bookings ending that month from now on. space-type (spaceType): its active
        /// spaces, in use now or not. occupied-month (month): spaces in use at the end of that month (now for this month).
        /// </summary>
        public static readonly IReadOnlyList<string> Kinds = new[]
        {
            "revenue-month", "aging-bucket", "top-customer", "new-bookings", "leases-ending", "space-type", "occupied-month"
        };
    }
}
