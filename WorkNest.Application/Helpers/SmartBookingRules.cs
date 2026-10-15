namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Rules of the customer "smart" booking (POST api/booking/smart, dbo.WN_Bookings_InsertSmart): which space a
    /// booking gets and how a private office is charged. The procedure applies them after QA_03_customer_booking; the
    /// API applies the same rules itself on a database without that update (see DbRepository.InsertSmartBookingAsync).
    /// </summary>
    public static class SmartBookingRules
    {
        /// <summary>The billing period WN_Bookings_InsertSmart books a category by: Monthly (offices, shared desks) or Hourly.</summary>
        public static string BookingPeriodCode(string? categoryCode) =>
            string.Equals(categoryCode, "PrivateOffice", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(categoryCode, "SharedSpace", StringComparison.OrdinalIgnoreCase)
                ? "Monthly"
                : "Hourly";

        /// <summary>A private office is rented whole: WN_Bookings_Insert charges seat price x the office's seats (@Capacity).</summary>
        public static bool IsPrivateOffice(string? categoryCode) =>
            string.Equals(categoryCode, "PrivateOffice", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(categoryCode, "Private", StringComparison.OrdinalIgnoreCase);

        /// <summary>Rows of WN_Bookings_GetSmartAvailable at one location (null = every row).</summary>
        public static List<IDictionary<string, object?>> AtLocation(IEnumerable<IDictionary<string, object?>> rows, int? locationId) =>
            locationId is null
                ? rows.ToList()
                : rows.Where(r => IntOf(r, "LocationId") == locationId).ToList();

        /// <summary>
        /// True when the row has an active price for the category's booking period (the "Bookable" column after QA_03;
        /// before it, the row's BillingPeriodCode).
        /// </summary>
        public static bool IsBookable(IDictionary<string, object?> row, string? categoryCode)
        {
            if (row.TryGetValue("Bookable", out var b) && b is not null && b is not DBNull)
                return Convert.ToBoolean(b);
            return string.Equals(TextOf(row, "BillingPeriodCode"), BookingPeriodCode(categoryCode), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The space a booking gets, from WN_Bookings_GetSmartAvailable's rows (free for the period, smallest first):
        /// the first bookable row at the location, else the first row there (it then fails on its missing price, as
        /// the procedure does). Null when the location has no free space.
        /// </summary>
        public static IDictionary<string, object?>? PickSpace(IEnumerable<IDictionary<string, object?>> rows, string? categoryCode, int? locationId)
        {
            var here = AtLocation(rows, locationId);
            return here.FirstOrDefault(r => IsBookable(r, categoryCode)) ?? here.FirstOrDefault();
        }

        /// <summary>Seats to charge for (WN_Bookings_Insert @Capacity): the office's seats for a private office, otherwise none.</summary>
        public static int? ChargedSeats(string? categoryCode, IDictionary<string, object?> space) =>
            IsPrivateOffice(categoryCode) && IntOf(space, "Capacity") is int seats && seats > 0 ? seats : null;

        /// <summary>WN_Bookings_InsertSmart returned the booking made a moment ago (IsDuplicate = 1), not a new one.</summary>
        public static bool IsDuplicate(IDictionary<string, object?>? result) =>
            result is not null && result.TryGetValue("IsDuplicate", out var d) && d is not null && d is not DBNull && Convert.ToBoolean(d);

        public const string DuplicateMessage =
            "You made this booking a moment ago, so no second booking was made. Here is that booking and its challan.";

        public const string NoSpaceMessage = "No available space for the requested period.";

        public static int? IntOf(IDictionary<string, object?> row, string key) =>
            row.TryGetValue(key, out var v) && v is not null && v is not DBNull && int.TryParse(Convert.ToString(v), out var i) ? i : null;

        private static string? TextOf(IDictionary<string, object?> row, string key) =>
            row.TryGetValue(key, out var v) && v is not null && v is not DBNull ? Convert.ToString(v) : null;
    }
}
