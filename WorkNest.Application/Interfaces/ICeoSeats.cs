using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Raw result sets of dbo.WN_Ceo_Seats (script SEAT_01_ceo_seats): [0] rooms with their shift seats, bookings and
    /// sold monthly rent now and their period figures, [1] the last 12 months. Throws
    /// <see cref="CeoSeatsDatabaseUpdateMissingException"/> when the procedure is not on the database yet.
    /// </summary>
    public interface ICeoSeatsRepository
    {
        /// <param name="from">With <paramref name="to"/> (days included): a custom range instead of the calendar period.</param>
        Task<List<List<IDictionary<string, object?>>>> GetSeatsAsync(int? locationId, int periodMonths, DateTime businessNow,
            DateTime? from = null, DateTime? to = null);
    }

    /// <summary>CEO dashboard Seats section (GET api/ceo/seats). Bad parameters give a failed ApiResponse (400).</summary>
    public interface ICeoSeatsService
    {
        /// <summary>
        /// period = month | quarter | year | custom (custom: <paramref name="from"/> / <paramref name="to"/>, yyyy-MM-dd).
        /// Before SEAT_01 a successful response with available = false and <see cref="CeoSeatsDatabaseUpdateMissingException.UserMessage"/>.
        /// </summary>
        Task<ApiResponse> GetSeatsAsync(string? period, int? locationId, string? from = null, string? to = null);
    }

    /// <summary>dbo.WN_Ceo_Seats is missing: the script SEAT_01_ceo_seats has not run on this database.</summary>
    public sealed class CeoSeatsDatabaseUpdateMissingException : Exception
    {
        public const string Update = "SEAT_01";

        /// <remarks>No word "SQL" in it: the CMS and the app hide any message that mentions it.</remarks>
        public const string UserMessage =
            "Database update needed: run the script SEAT_01_ceo_seats_TEST (SEAT_01_ceo_seats_LIVE on the live database) to see the seat figures.";

        public CeoSeatsDatabaseUpdateMissingException(Exception? inner) : base(UserMessage, inner)
        {
        }
    }
}
