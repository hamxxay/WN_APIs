using System.Text.Json;
using System.Text.RegularExpressions;

namespace WorkNest.Application.InvoiceContent
{
    public sealed record InvoiceContentValidation(InvoiceContentFields? Content, IReadOnlyList<string> Errors)
    {
        public bool IsValid => Errors.Count == 0 && Content != null;
    }

    /// <summary>Server-side validation of submitted content: known fields only, right kinds, lengths, plain text, allowed tokens.</summary>
    public static class InvoiceContentValidator
    {
        private static readonly Regex HtmlLike = new(@"<\s*/?\s*[a-zA-Z!]", RegexOptions.Compiled);
        private static readonly Regex Token = new(@"\{[^{}]*\}", RegexOptions.Compiled);
        private static readonly Regex Control = new(@"[\u0000-\u001F\u007F]", RegexOptions.Compiled);

        /// <summary>Validates the "content" object of a request body ({ key: string | string[] }).</summary>
        public static InvoiceContentValidation Validate(JsonElement? content)
        {
            var errors = new List<string>();
            if (content is null || content.Value.ValueKind != JsonValueKind.Object)
                return new InvoiceContentValidation(null, new[] { "content: send the invoice content as an object." });

            var fields = new InvoiceContentFields();
            foreach (var p in content.Value.EnumerateObject())
            {
                var def = InvoiceContentSchema.Find(p.Name);
                if (def == null) { errors.Add($"{p.Name}: is not a known invoice content field."); continue; }
                if (def.Kind == InvoiceContentFieldKind.List)
                {
                    if (p.Value.ValueKind != JsonValueKind.Array || p.Value.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String))
                    { errors.Add($"{def.Label}: must be a list of text lines."); continue; }
                    fields.Lists[def.Key] = p.Value.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
                }
                else
                {
                    if (p.Value.ValueKind == JsonValueKind.Null) { fields.Text[def.Key] = ""; continue; }
                    if (p.Value.ValueKind != JsonValueKind.String) { errors.Add($"{def.Label}: must be text."); continue; }
                    fields.Text[def.Key] = p.Value.GetString() ?? "";
                }
            }
            if (errors.Count > 0) return new InvoiceContentValidation(null, errors);
            return Validate(fields);
        }

        /// <summary>Validates and normalises (trims blanks around text, drops empty list lines, line breaks inside a line are refused).</summary>
        public static InvoiceContentValidation Validate(InvoiceContentFields input)
        {
            var errors = new List<string>();
            var o = new InvoiceContentFields();
            foreach (var def in InvoiceContentSchema.AllFields)
            {
                if (def.Kind == InvoiceContentFieldKind.List)
                {
                    var items = input.GetList(def.Key).Select(s => (s ?? "").Trim()).Where(s => s.Length > 0).ToList();
                    if (def.Required && items.Count == 0) errors.Add($"{def.Label}: add at least one line.");
                    if (items.Count > InvoiceContentSchema.MaxListItems) errors.Add($"{def.Label}: at most {InvoiceContentSchema.MaxListItems} lines.");
                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        string body = item;
                        if (item.StartsWith("@", StringComparison.Ordinal))
                        {
                            if (def.AllowsDepositTag && item.StartsWith(InvoiceContentSchema.DepositTag, StringComparison.Ordinal))
                                body = item.Substring(InvoiceContentSchema.DepositTag.Length).Trim();
                            else { errors.Add($"{def.Label}, line {i + 1}: a line cannot start with '@'" + (def.AllowsDepositTag ? " (except '@deposit ')." : ".")); continue; }
                        }
                        if (body.Length == 0) errors.Add($"{def.Label}, line {i + 1}: the line has no text.");
                        CheckText(def, body, $"{def.Label}, line {i + 1}", errors, null);
                        if (item.Length > def.MaxLength) errors.Add($"{def.Label}, line {i + 1}: at most {def.MaxLength} characters.");
                    }
                    o.Lists[def.Key] = items;
                }
                else
                {
                    var v = (input.Get(def.Key) ?? "").Trim();
                    if (def.Required && v.Length == 0) errors.Add($"{def.Label}: is required.");
                    if (v.Length > def.MaxLength) errors.Add($"{def.Label}: at most {def.MaxLength} characters.");
                    CheckText(def, v, def.Label, errors, def.Tokens ?? Array.Empty<string>());
                    if (v.Length > 0 && def.Tokens is { Length: > 0 } && !def.Tokens.All(t => v.Contains(t, StringComparison.Ordinal)))
                        errors.Add($"{def.Label}: must contain {string.Join(", ", def.Tokens)} where the value goes.");
                    o.Text[def.Key] = v;
                }
            }

            bool hasPayment = !string.IsNullOrWhiteSpace(o.Get(InvoiceContentKeys.OneBillWithNumber))
                || !string.IsNullOrWhiteSpace(o.Get(InvoiceContentKeys.OneBillWithoutNumber))
                || !string.IsNullOrWhiteSpace(o.Get(InvoiceContentKeys.BankName))
                || !string.IsNullOrWhiteSpace(o.Get(InvoiceContentKeys.AccountNumber))
                || !string.IsNullOrWhiteSpace(o.Get(InvoiceContentKeys.Iban));
            if (!hasPayment)
                errors.Add("Payment instructions: enter a 1Bill line, or the bank name, account number or IBAN, so the invoice tells the customer how to pay.");

            return new InvoiceContentValidation(errors.Count == 0 ? o : null, errors);
        }

        private static void CheckText(InvoiceContentFieldDef def, string v, string label, List<string> errors, string[]? allowedTokens)
        {
            if (v.Length == 0) return;
            if (Control.IsMatch(v)) errors.Add($"{label}: must be one line of plain text (no line breaks or control characters).");
            if (HtmlLike.IsMatch(v)) errors.Add($"{label}: plain text only (no HTML tags).");
            foreach (Match m in Token.Matches(v))
                if (allowedTokens == null || !allowedTokens.Contains(m.Value, StringComparer.Ordinal))
                    errors.Add($"{label}: '{m.Value}' is not allowed here" + (allowedTokens is { Length: > 0 } ? $" (only {string.Join(", ", allowedTokens)})." : " (curly brackets are not used in this field)."));
            if (v.Contains('{') != v.Contains('}')) errors.Add($"{label}: unbalanced curly bracket.");
        }
    }
}
