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
        public static IReadOnlyList<LocationGroup> Group(IEnumerable<DiscountCapRow> rows) => Group(rows, Array.Empty<(int Id, string Name)>());

        /// <summary>The offering types per location, plus every active location that has none (Items empty): a location without
        /// offering types cannot have a manual discount, so the page shows it instead of hiding it.</summary>
        public static IReadOnlyList<LocationGroup> Group(IEnumerable<DiscountCapRow> rows, IEnumerable<(int Id, string Name)> locations)
        {
            var groups = rows.GroupBy(r => r.LocationId)
                .Select(g => new LocationGroup(
                    g.Key,
                    g.Key == null ? "All locations" : (g.Select(r => r.LocationName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? $"Location {g.Key}"),
                    g.OrderBy(r => r.Id).Select(ToItem).ToList()))
                .ToList();
            foreach (var (id, name) in locations)
                if (!groups.Any(g => g.LocationId == id))
                    groups.Add(new LocationGroup(id, string.IsNullOrWhiteSpace(name) ? $"Location {id}" : name, Array.Empty<Item>()));
            return groups.OrderBy(g => g.LocationId == null ? 1 : 0).ThenBy(g => g.LocationId).ToList();
        }
    }
}
