using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>Pure rules of the Discount Caps page: the check on a new cap and the grouping of offering types by location.</summary>
    public static class DiscountCapRules
    {
        /// <summary>Plain-English check of a new cap (0 to 100, at most 2 decimals). Null error = fine.</summary>
        public static string? Validate(decimal? discountCap)
        {
            if (discountCap == null) return "Enter the new discount cap.";
            if (discountCap < 0m || discountCap > 100m) return "The discount cap must be between 0 and 100.";
            if (decimal.Round(discountCap.Value, 2) != discountCap.Value) return "The discount cap can have at most 2 decimals.";
            return null;
        }

        public sealed record Item(int Id, string Description, decimal DiscountCap, bool IsActive, string? UpdatedBy, DateTime? UpdatedAt);
        public sealed record LocationGroup(int? LocationId, string LocationName, IReadOnlyList<Item> Items);

        public static Item ToItem(DiscountCapRow r) => new(r.Id, r.Description, r.DiscountCap, r.IsActive, r.UpdatedBy, r.UpdatedAt);

        /// <summary>One group per location (by location id, locations without a row last as "All locations"), items by id.</summary>
        public static IReadOnlyList<LocationGroup> Group(IEnumerable<DiscountCapRow> rows) =>
            rows.GroupBy(r => r.LocationId)
                .OrderBy(g => g.Key == null ? 1 : 0).ThenBy(g => g.Key)
                .Select(g => new LocationGroup(
                    g.Key,
                    g.Key == null ? "All locations" : (g.Select(r => r.LocationName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? $"Location {g.Key}"),
                    g.OrderBy(r => r.Id).Select(ToItem).ToList()))
                .ToList();
    }
}
