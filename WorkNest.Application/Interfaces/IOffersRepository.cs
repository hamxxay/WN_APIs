using WorkNest.Application.DTOs.Offers;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Validated values of an offer to save (dates are Pakistan wall-clock, Kind Unspecified).</summary>
    public sealed record OfferSaveCommand(
        string Name, string DiscountType, decimal DiscountValue, DateTime StartDate, DateTime EndDate,
        int? LocationId, string ApplyTo, bool ApplyToDeposit, bool IsActive);

    /// <summary>
    /// Offers (script OFFER_01_offers: dbo.WN_Offers and its procedures). The admin calls (list / save / deactivate) throw
    /// <see cref="OffersMissingException"/> before the script has run; the lookups the quotation / booking / invoice flows use
    /// (<see cref="GetRunningAsync"/>, <see cref="GetSnapshotsAsync"/>) never throw for a missing table / column / procedure:
    /// they answer "no offer", so the API keeps working on a server where the script has not run.
    /// </summary>
    public interface IOffersRepository
    {
        Task<IReadOnlyList<OfferDto>> GetListAsync(CancellationToken ct = default);
        /// <summary>Creates (<paramref name="offerId"/> null) or updates; returns the OfferId. A rule broken in the procedure throws <see cref="OfferRuleException"/>.</summary>
        Task<int> SaveAsync(int? offerId, OfferSaveCommand command, string? userEmail, CancellationToken ct = default);
        Task DeactivateAsync(int offerId, string? userEmail, CancellationToken ct = default);

        /// <summary>The offer running at <paramref name="at"/> (null = now, Pakistan time) for this location, or null. Never throws for a missing script.</summary>
        Task<OfferDto?> GetRunningAsync(int? locationId, DateTime? at, CancellationToken ct = default);

        /// <summary>The offer snapshot columns of these rows (only rows with an offer are returned). Empty when the columns do not exist yet.</summary>
        Task<IReadOnlyDictionary<int, OfferSnapshot>> GetSnapshotsAsync(OfferEntity entity, IEnumerable<int> ids, CancellationToken ct = default);
    }

    /// <summary>The script OFFER_01_offers has not run (SQL errors 208 / 2812).</summary>
    public sealed class OffersMissingException : Exception
    {
        public const string UserMessage = "Offers are not set up on this server yet.";
        public OffersMissingException(Exception? inner = null) : base(UserMessage, inner) { }
    }

    /// <summary>A rule of the save procedure (THROW 50000): the message is plain English for the CMS.</summary>
    public sealed class OfferRuleException : Exception
    {
        public OfferRuleException(string message) : base(message) { }
    }
}
