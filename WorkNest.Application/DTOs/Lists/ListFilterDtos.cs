namespace WorkNest.Application.DTOs.Lists
{
    /// <summary>
    /// Optional filters of the admin lists (customers, bookings, quotations, invoices, payments; script LST_01_list_filters):
    /// a status (the value of a choice from GET api/{list}/statuses) and a date range in business (Pakistan) dates,
    /// both ends included. Which date: bookings = start date, quotations = quotation date, invoices = issue date,
    /// payments = payment date, customers = created date. Empty = the list exactly as before (the old procedures).
    /// </summary>
    public sealed class ListFilter
    {
        public string? Status { get; init; }
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }

        /// <summary>A status or a date is chosen (the new LST_01 procedures are needed).</summary>
        public bool HasStatusOrDates => !string.IsNullOrWhiteSpace(Status) || From.HasValue || To.HasValue;
    }

    /// <summary>One choice of a list's Status filter: Value is sent back as ?status=, Label is shown.</summary>
    public sealed class ListStatusOption
    {
        public string Value { get; init; } = "";
        public string Label { get; init; } = "";
    }

    /// <summary>The bounds of a date filter as the database column holds them: Start included, End excluded (null = open).</summary>
    public readonly record struct ListDateRange(DateTime? Start, DateTime? End);
}
