using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Raw result sets of dbo.WN_Ceo_Discounts (script DD_01_ceo_discounts): [0] the confirmed bookings on the rooms sold
    /// monthly running in the period, [1] the invoices with a discount issued in it. Throws
    /// <see cref="CeoDiscountsUpdateMissingException"/> while the procedure is not on the database.
    /// </summary>
    public interface ICeoDiscountsRepository
    {
        /// <param name="from">With <paramref name="to"/> (days included): a custom range instead of the calendar period.</param>
        Task<List<List<IDictionary<string, object?>>>> GetDiscountsAsync(int? locationId, int periodMonths, DateTime businessNow,
            IReadOnlyCollection<int> voidStatusIds, DateTime? from = null, DateTime? to = null);
    }

    /// <summary>
    /// The Discounts section of the CEO dashboard (api/ceo/discounts): the discount a month given on the bookings
    /// running in the period (below the expected list / shift price, plus fixed PKR discounts), the discount on the
    /// invoices issued in it, by location, by the person who sold the booking and the biggest ones; and the records
    /// behind them. Read-only. Bad parameters give a failed ApiResponse (400).
    /// </summary>
    public interface ICeoDiscountsService
    {
        /// <summary>Before DD_01: a successful response with available = false and a message.</summary>
        Task<ApiResponse> GetDiscountsAsync(string? period, int? locationId, string? from = null, string? to = null);

        /// <summary>kind = bookings | invoices. Before DD_01: <see cref="CeoDiscountsUpdateMissingException"/>.</summary>
        Task<ApiResponse> GetRecordsAsync(string kind, CeoDiscountRecordsQuery query);
    }

    /// <summary>dbo.WN_Ceo_Discounts is missing: the database update DD_01 has not run.</summary>
    public sealed class CeoDiscountsUpdateMissingException : Exception
    {
        /// <summary>Script DD_01_ceo_discounts (TEST / LIVE).</summary>
        public const string Update = "DD_01";

        /// <remarks>No ".sql" and no "sql" in it: the CMS and the app hide any message that mentions SQL.</remarks>
        public const string UserMessage =
            "Database update needed: run the script DD_01_ceo_discounts_TEST (DD_01_ceo_discounts_LIVE on the live database) to see the discounts.";

        public CeoDiscountsUpdateMissingException(Exception? inner) : base(UserMessage, inner)
        {
        }
    }
}
