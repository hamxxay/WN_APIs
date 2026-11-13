namespace WorkNest.Application.Interfaces
{
    /// <summary>One dbo.WN_OfferingType row as the Discount Caps page needs it. UpdatedBy / UpdatedAt are null when the columns do not exist yet (script DISC_01).</summary>
    public sealed record DiscountCapRow(int Id, string Description, decimal DiscountCap, bool IsActive, int? LocationId, string? LocationName, string? UpdatedBy, DateTime? UpdatedAt);

    /// <summary>Reads and changes the discount cap (dbo.WN_OfferingType.DiscountCap). Works before DISC_01 runs (no UpdatedBy / UpdatedAt then).</summary>
    public interface IDiscountCapsRepository
    {
        Task<IReadOnlyList<DiscountCapRow>> GetAllAsync(CancellationToken ct = default);

        /// <summary>Every active location (id, name), so a location without offering types is still listed.</summary>
        Task<IReadOnlyList<(int Id, string Name)>> GetLocationsAsync(CancellationToken ct = default);

        /// <summary>Sets the cap; null when the row does not exist. Returns the saved row.</summary>
        Task<DiscountCapRow?> UpdateAsync(int id, decimal discountCap, string? userEmail, CancellationToken ct = default);
    }

    /// <summary>The offering types table is missing (SQL errors 208 / 2812): answered as 503.</summary>
    public sealed class DiscountCapsMissingException : Exception
    {
        public const string UserMessage = "Discount caps are not available on this server yet.";
        public DiscountCapsMissingException(Exception? inner = null) : base(UserMessage, inner) { }
    }
}
