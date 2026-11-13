using System.Text.Json;
using System.Text.RegularExpressions;

namespace WorkNest.Application.InvoiceContent
{
    /// <summary>
    /// The configurable text of one invoice-content version: plain text fields and ordered lists, each mapped to an existing spot
    /// of the invoice PDFs (see <see cref="InvoiceContentSchema"/>). Stored as JSON, served to the CMS as { key: string | string[] }.
    /// </summary>
    public sealed class InvoiceContentFields
    {
        public Dictionary<string, string> Text { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> Lists { get; } = new(StringComparer.Ordinal);

        public string Get(string key) => Text.TryGetValue(key, out var v) ? v ?? string.Empty : string.Empty;
        public IReadOnlyList<string> GetList(string key) => Lists.TryGetValue(key, out var v) ? v : Array.Empty<string>();

        public InvoiceContentFields Clone()
        {
            var c = new InvoiceContentFields();
            foreach (var kv in Text) c.Text[kv.Key] = kv.Value;
            foreach (var kv in Lists) c.Lists[kv.Key] = new List<string>(kv.Value);
            return c;
        }

        /// <summary>Every field in schema order (missing ones as empty), so the JSON is stable.</summary>
        public Dictionary<string, object> ToApi()
        {
            var o = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var f in InvoiceContentSchema.AllFields)
                o[f.Key] = f.Kind == InvoiceContentFieldKind.List ? GetList(f.Key).ToArray() : Get(f.Key);
            return o;
        }

        private static readonly JsonSerializerOptions StoredJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>Stored form (plain characters such as &amp; and + are not escaped, so the seed in the script stays readable).</summary>
        public string ToJson() => JsonSerializer.Serialize(ToApi(), StoredJson);

        /// <summary>Reads stored JSON. Lenient: unknown keys are ignored, wrong kinds skipped (the validator is the strict one).</summary>
        public static InvoiceContentFields FromJson(string? json)
        {
            var c = new InvoiceContentFields();
            if (string.IsNullOrWhiteSpace(json)) return c;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return c;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var def = InvoiceContentSchema.Find(p.Name);
                if (def == null) continue;
                if (def.Kind == InvoiceContentFieldKind.List && p.Value.ValueKind == JsonValueKind.Array)
                    c.Lists[def.Key] = p.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? "").ToList();
                else if (def.Kind != InvoiceContentFieldKind.List && p.Value.ValueKind == JsonValueKind.String)
                    c.Text[def.Key] = p.Value.GetString() ?? "";
            }
            return c;
        }

        private static readonly Regex PlaceholderPattern = new(@"\[[^\[\]]+\]", RegexOptions.Compiled);

        /// <summary>Keys of the fields that still hold a "[...]" placeholder the staff must replace before using those details.</summary>
        public List<string> PlaceholderKeys()
        {
            var keys = new List<string>();
            foreach (var f in InvoiceContentSchema.AllFields)
            {
                bool has = f.Kind == InvoiceContentFieldKind.List
                    ? GetList(f.Key).Any(i => PlaceholderPattern.IsMatch(i))
                    : PlaceholderPattern.IsMatch(Get(f.Key));
                if (has) keys.Add(f.Key);
            }
            return keys;
        }
    }

    /// <summary>The content a PDF is rendered with, and which version it came from.</summary>
    public sealed class ResolvedInvoiceContent
    {
        public InvoiceContentFields Fields { get; init; } = new();
        /// <summary>The location whose content this is (an unknown location uses Location 1's).</summary>
        public int LocationId { get; init; }
        /// <summary>Row id of WN_InvoiceContentVersions (what WN_Invoices.InvoiceContentVersionId holds); null for the built-in content.</summary>
        public int? VersionId { get; init; }
        public int? VersionNo { get; init; }
        /// <summary>dbo.WN_Locations.Address of the invoice's authoritative location; null when it has no row / no address.</summary>
        public string? LocationAddress { get; set; }
        /// <summary>The header address to print: the location's address, else <paramref name="current"/> (the invoice's own value), else today's default text.</summary>
        public string AddressOr(string? current) => InvoiceVendorAddress.Choose(LocationAddress, current);
        /// <summary>True when the tables are not set up (script INVC_01 not run): today's hardcoded wording.</summary>
        public bool IsBuiltIn => VersionId == null;
    }

    /// <summary>The company address line of the invoice header (second line of the top-right block): one rule for every invoice render path.</summary>
    public static class InvoiceVendorAddress
    {
        /// <summary>Today's text, used when the location has no (or an empty) address.</summary>
        public const string Default = "3rd Floor EOBI Building-II, I-8 Markaz, Islamabad";

        public static string Choose(string? locationAddress, string? current)
            => !string.IsNullOrWhiteSpace(locationAddress) ? locationAddress.Trim()
             : !string.IsNullOrWhiteSpace(current) ? current
             : Default;

        /// <summary>WN_Locations.Address of the location (parameterized read); null for no location, no row, an empty address or any read failure (the header then keeps today's text).</summary>
        public static async Task<string?> LookupAsync(IInvoiceContentRepository repo, int? locationId, CancellationToken ct = default)
        {
            if (locationId is not > 0) return null;
            try { var a = await repo.GetLocationAddressAsync(locationId.Value, ct); return string.IsNullOrWhiteSpace(a) ? null : a.Trim(); }
            catch (Exception) when (!ct.IsCancellationRequested) { return null; }
        }
    }
}
