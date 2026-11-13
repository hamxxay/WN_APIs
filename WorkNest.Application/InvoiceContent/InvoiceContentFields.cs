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
        /// <summary>True when the tables are not set up (script INVC_01 not run): today's hardcoded wording.</summary>
        public bool IsBuiltIn => VersionId == null;
    }
}
