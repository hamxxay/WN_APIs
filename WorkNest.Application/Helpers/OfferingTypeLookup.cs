using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Which dbo.WN_OfferingType row (its DiscountCap) applies to a quotation / booking. Match by id AND the space's location; fall back to the
    /// same id at any location; no other fallback (no first row, no hardcoded cap): when nothing matches, a manual discount cannot be checked
    /// and is refused with <see cref="NotSetUpMessage"/>.
    /// </summary>
    public static class OfferingTypeLookup
    {
        public const string NotSetUpMessage = "The discount limit for this offering type is not set up. Ask a super admin.";

        private static int? IdOf(IDictionary<string, object?> row) =>
            row.TryGetValue("Id", out var v) && v is not null && v is not DBNull ? Convert.ToInt32(v) : null;

        private static int? LocationOf(IDictionary<string, object?> row) =>
            row.TryGetValue("LocationId", out var v) && v is not null && v is not DBNull ? Convert.ToInt32(v) : null;

        private static IDictionary<string, object?>? Prefer(IEnumerable<IDictionary<string, object?>> candidates, int? locationId)
        {
            var list = candidates.ToList();
            if (list.Count == 0) return null;
            return (locationId.HasValue ? list.FirstOrDefault(o => LocationOf(o) == locationId) : null) ?? list.First();
        }

        /// <summary>
        /// <paramref name="id"/> = OfferingTypeId; <paramref name="text"/> = the OfferingType text (an id, or a description such as "24/7").
        /// Returns the row or null (nothing matched).
        /// </summary>
        public static IDictionary<string, object?>? Find(IEnumerable<IDictionary<string, object?>> types, int? id, string? text, int? locationId)
        {
            var all = types.ToList();
            if (id is > 0)
                return Prefer(all.Where(o => IdOf(o) == id), locationId);

            if (!string.IsNullOrWhiteSpace(text))
            {
                if (int.TryParse(text, out var parsed))
                    return Prefer(all.Where(o => IdOf(o) == parsed), locationId);
                var exact = Prefer(all.Where(o => string.Equals(o.TryGetValue("Description", out var d) ? d?.ToString() : null, text, StringComparison.OrdinalIgnoreCase)), locationId);
                if (exact != null) return exact;
                return Prefer(all.Where(o => (o.TryGetValue("Description", out var d) ? d?.ToString() ?? "" : "").StartsWith(text, StringComparison.OrdinalIgnoreCase)), locationId);
            }
            return null;
        }

        /// <summary>The row's DiscountCap, or null when there is no row / no cap value.</summary>
        public static decimal? CapOf(IDictionary<string, object?>? row) =>
            row != null && row.TryGetValue("DiscountCap", out var v) && v is not null && v is not DBNull ? Convert.ToDecimal(v) : null;
    }
}
