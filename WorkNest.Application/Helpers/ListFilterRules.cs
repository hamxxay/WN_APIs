using WorkNest.Application.DTOs.Lists;
using WorkNest.Domain.Enums;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Turns the admin list filters (?status=, ?from=, ?to=) into the parameters of the LST_01 list procedures.
    /// Wrong values throw <see cref="ListFilterException"/> (answered 400), so a filter is never silently ignored.
    /// </summary>
    public static class ListFilterRules
    {
        /// <summary>The list names used by GET api/{list}/statuses and dbo.WN_ListStatusOptions_Get.</summary>
        public const string Bookings = "bookings", Quotations = "quotations", Invoices = "invoices", Payments = "payments", Customers = "customers";

        /// <summary>The older "pending" booking status still written on new bookings (it has no WN_BookingStatuses row).</summary>
        public const int LegacyPendingBookingStatusId = 1;

        private const int MaxTextStatusLength = 50;

        /// <summary>
        /// [Start, End) of a from / to date filter (business dates, both included). <paramref name="utcColumn"/>: the
        /// column holds UTC (CreatedOn, CreatedAt, PaidOn ...), so the business-day bounds are converted to UTC.
        /// </summary>
        public static ListDateRange DateRange(DateTime? from, DateTime? to, bool utcColumn, TimeZoneInfo zone)
        {
            if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
                throw new ListFilterException("The From date is after the To date.");
            DateTime? start = from?.Date;
            DateTime? end = to?.Date.AddDays(1);
            if (utcColumn)
            {
                start = start.HasValue ? ToUtc(start.Value, zone) : null;
                end = end.HasValue ? ToUtc(end.Value, zone) : null;
            }
            return new ListDateRange(start, end);
        }

        private static DateTime ToUtc(DateTime businessTime, TimeZoneInfo zone) =>
            DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(businessTime, DateTimeKind.Unspecified), zone), DateTimeKind.Unspecified);

        /// <summary>
        /// Booking status ids (CSV) for a booking status id from WN_BookingStatuses; Pending also covers the older
        /// pending value 1 (same rule as the auto-confirm job). Null = no status chosen.
        /// </summary>
        public static string? BookingStatusIds(string? status)
        {
            var id = IntStatus(status);
            if (id == null) return null;
            return id == (int)BookingStatus.Pending ? $"{LegacyPendingBookingStatusId},{id}" : id.Value.ToString();
        }

        /// <summary>A payment StatusId (whole number). Null = no status chosen.</summary>
        public static int? PaymentStatusId(string? status) => IntStatus(status);

        /// <summary>Customers: "1" = active, "0" = inactive. Null = both.</summary>
        public static bool? CustomerIsActive(string? status)
        {
            var s = status?.Trim();
            if (string.IsNullOrEmpty(s)) return null;
            return s switch
            {
                "1" => true,
                "0" => false,
                _ => throw new ListFilterException("Unknown status.")
            };
        }

        /// <summary>A status given as text (quotations, invoices): trimmed, at most 50 characters. Null = none chosen.</summary>
        public static string? TextStatus(string? status)
        {
            var s = status?.Trim();
            if (string.IsNullOrEmpty(s)) return null;
            if (s.Length > MaxTextStatusLength) throw new ListFilterException("Unknown status.");
            return s;
        }

        /// <summary>Location ids as CSV for the procedures (null = every location).</summary>
        public static string? LocationCsv(IReadOnlyCollection<int>? locationIds) =>
            locationIds == null ? null : locationIds.Count == 0 ? "-1" : string.Join(",", locationIds.Distinct());

        private static int? IntStatus(string? status)
        {
            var s = status?.Trim();
            if (string.IsNullOrEmpty(s)) return null;
            if (!int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) || id < 0)
                throw new ListFilterException("Unknown status.");
            return id;
        }
    }

    /// <summary>A list filter value that cannot be used (answered 400 with the message).</summary>
    public sealed class ListFilterException : Exception
    {
        public ListFilterException(string message) : base(message) { }
    }

    /// <summary>
    /// The LST_01 procedures are not on this database yet (SQL error 2812 procedure missing, 8144 / 8145 parameters
    /// unknown). Answered 503 with <see cref="UserMessage"/>. The lists without the new filters keep working.
    /// </summary>
    public sealed class ListFiltersDatabaseUpdateMissingException : Exception
    {
        public const string Script = "LST_01_list_filters";
        // No ".sql" in the text: the CMS and the app hide any message with the word "sql" in it.
        public const string UserMessage = "Database update needed: run the script " + Script + "_TEST (" + Script + "_LIVE on the live database).";

        public ListFiltersDatabaseUpdateMissingException(Exception inner) : base(UserMessage, inner)
        {
            Detail = inner.Message;
        }

        /// <summary>The database's own error text (for the server log).</summary>
        public string Detail { get; }
    }
}
