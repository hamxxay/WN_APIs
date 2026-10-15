using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WorkNest.Application.DTOs.Contact;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// The WhatsApp message staff send to a tour inquiry (whatsapp-template contract, 15 Oct 2026): a template that admins
    /// edit in the CMS (dbo.WN_MessageTemplates, key <see cref="TemplateKey"/>, script WT_01_whatsapp_template), filled in
    /// from THAT inquiry and the signed-in staff member. The one renderer of the API: the CMS preview and
    /// GET api/contact/{publicId}/whatsapp-message both use it.
    /// Rules:
    ///   * {placeholder} (case-insensitive): name, staff, branch, date, source, workspace, seats (<see cref="Placeholders"/>).
    ///   * [ ... ] = an optional part: kept (without the brackets) only when EVERY placeholder inside it has a value,
    ///     otherwise removed completely. Optional parts cannot be inside each other.
    ///   * Outside [ ]: an empty placeholder becomes "", except {name} -> "there" and {staff} -> "the WorkNest team".
    ///   * Afterwards: double spaces collapsed, no space before , . ! ? ; :, every line trimmed, no more than one empty
    ///     line in a row, at most <see cref="MaxMessageLength"/> characters.
    ///   * Literal [ ] { } are not supported (<see cref="Validate"/> refuses them; the renderer leaves stray ones as text).
    /// </summary>
    public static class InquiryMessageRenderer
    {
        public const string TemplateKey = "inquiry_whatsapp";
        public const int MaxTemplateLength = 2000;
        public const int MaxMessageLength = 1000;
        public const string NameFallback = "there";
        public const string StaffFallback = "the WorkNest team";

        /// <summary>The wording used until an admin saves another one (also seeded by WT_01).</summary>
        public const string DefaultTemplate =
            "Hi {name}, this is {staff} from WorkNest[ {branch}].\n" +
            "Thank you for your tour inquiry[ on {date}][ (via {source})][ for a {workspace}][ for {seats} people].\n" +
            "When would you like to visit?";

        /// <summary>The placeholders, in the order the CMS shows them (name without braces, lower case).</summary>
        public static readonly IReadOnlyList<MessagePlaceholder> Placeholders = new List<MessagePlaceholder>
        {
            new("name", "The customer's first name (\"there\" when the inquiry has no name)"),
            new("staff", "Your first name, the staff member sending the message (\"the WorkNest team\" when unknown)"),
            new("branch", "The branch the inquiry is for, e.g. I-8"),
            new("date", "The day the inquiry came in, e.g. 9 Oct (with the year when it is not this year)"),
            new("source", "How it came in: our website, WhatsApp, a walk-in visit or a reference (from ...)"),
            new("workspace", "The workspace they asked about, e.g. Private Office"),
            new("seats", "How many people they asked for, e.g. 3"),
        };

        private static readonly HashSet<string> Known =
            new(Placeholders.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        // ---- Validation (PUT / preview) ---------------------------------------------------------------------------

        /// <summary>A plain-language reason the template can't be saved, or null when it is fine.</summary>
        public static string? Validate(string? template)
        {
            if (string.IsNullOrWhiteSpace(template)) return "Enter the message.";
            if (template.Length > MaxTemplateLength) return $"The message can be at most {MaxTemplateLength} characters.";
            var inOptional = false;
            for (var i = 0; i < template.Length; i++)
            {
                switch (template[i])
                {
                    case '[':
                        if (inOptional) return "An optional part in [ ] can't contain another [.";
                        inOptional = true;
                        break;
                    case ']':
                        if (!inOptional) return "There is a ] without a matching [.";
                        inOptional = false;
                        break;
                    case '{':
                        var close = template.IndexOf('}', i + 1);
                        var next = template.IndexOf('{', i + 1);
                        if (close < 0 || (next >= 0 && next < close)) return "Every { needs a matching } (for example {name}).";
                        var name = template.Substring(i + 1, close - i - 1);
                        if (!Known.Contains(name)) return $"{{{name}}} is not a placeholder. Use {KnownList()}.";
                        i = close;
                        break;
                    case '}':
                        return "There is a } without a matching {.";
                }
            }
            return inOptional ? "Every [ needs a matching ] (an optional part ends with ])." : null;
        }

        private static string KnownList()
        {
            var names = Placeholders.Select(p => "{" + p.Name + "}").ToList();
            return string.Join(", ", names.Take(names.Count - 1)) + " or " + names[^1];
        }

        // ---- Rendering --------------------------------------------------------------------------------------------

        /// <summary>The template filled in with <paramref name="values"/>; an empty template = <see cref="DefaultTemplate"/>.</summary>
        public static string Render(string? template, InquiryMessageValues values)
        {
            if (string.IsNullOrWhiteSpace(template)) template = DefaultTemplate;
            var map = values.ToMap();
            var sb = new StringBuilder(template.Length + 64);
            var i = 0;
            while (i < template.Length)
            {
                var c = template[i];
                if (c == '[')
                {
                    var end = template.IndexOf(']', i + 1);
                    var nested = end < 0 ? -1 : template.IndexOf('[', i + 1, end - i - 1);
                    if (end > i && nested < 0)
                    {
                        var part = Fill(template.Substring(i + 1, end - i - 1), map, optional: true);
                        if (part != null) sb.Append(part);
                        i = end + 1;
                        continue;
                    }
                    sb.Append(c);   // stray [ : kept as text
                    i++;
                    continue;
                }
                if (c == '{' && TryPlaceholder(template, i, out var name, out var after))
                {
                    sb.Append(Value(map, name, optional: false));
                    i = after;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return Tidy(sb.ToString());
        }

        /// <summary>Placeholders filled in; null when <paramref name="optional"/> and one of them has no value.</summary>
        private static string? Fill(string text, IReadOnlyDictionary<string, string?> map, bool optional)
        {
            var sb = new StringBuilder(text.Length + 32);
            var i = 0;
            while (i < text.Length)
            {
                if (text[i] == '{' && TryPlaceholder(text, i, out var name, out var after))
                {
                    var value = Value(map, name, optional);
                    if (value == null) return null;
                    sb.Append(value);
                    i = after;
                    continue;
                }
                sb.Append(text[i]);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>A known {placeholder} starting at <paramref name="start"/>: its lower-case name and the index after "}".</summary>
        private static bool TryPlaceholder(string text, int start, out string name, out int after)
        {
            name = "";
            after = start;
            var close = text.IndexOf('}', start + 1);
            if (close < 0) return false;
            var candidate = text.Substring(start + 1, close - start - 1);
            if (!Known.Contains(candidate)) return false;
            name = candidate.ToLowerInvariant();
            after = close + 1;
            return true;
        }

        /// <summary>The value; in an optional part null when empty, outside one "" (name / staff: their fallbacks).</summary>
        private static string? Value(IReadOnlyDictionary<string, string?> map, string name, bool optional)
        {
            var value = map.TryGetValue(name, out var v) ? v : null;
            if (!string.IsNullOrEmpty(value)) return value;
            if (optional) return null;
            return name switch { "name" => NameFallback, "staff" => StaffFallback, _ => "" };
        }

        private static readonly Regex Spaces = new(@"[ \t ]{2,}", RegexOptions.Compiled);
        private static readonly Regex SpaceBeforePunctuation = new(@"[ \t ]+(?=[,.!?;:])", RegexOptions.Compiled);
        private static readonly Regex BlankLines = new(@"\n{3,}", RegexOptions.Compiled);

        /// <summary>Double spaces collapsed, no space before punctuation, lines trimmed, at most <see cref="MaxMessageLength"/> characters.</summary>
        public static string Tidy(string text)
        {
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .Select(line => SpaceBeforePunctuation.Replace(Spaces.Replace(line, " "), "").Trim());
            var result = BlankLines.Replace(string.Join("\n", lines), "\n\n").Trim();
            return result.Length <= MaxMessageLength ? result : result[..MaxMessageLength].TrimEnd();
        }

        // ---- The values of one inquiry ----------------------------------------------------------------------------

        /// <summary>
        /// The placeholder values of one tour inquiry row (dbo.WN_Contacts_GetList: Name, Message, InquiryType,
        /// ReferenceName, LocationName, CreatedOn) for the staff member (WN_Users.Name, else their email).
        /// <paramref name="today"/> = the business date (Pakistan); <paramref name="toLocal"/> turns a UTC CreatedOn into it.
        /// </summary>
        public static InquiryMessageValues FromInquiry(IDictionary<string, object?> row, string? staffName, string? staffEmail,
            DateTime today, Func<DateTime, DateTime>? toLocal = null)
        {
            var message = Text(row, "Message");
            DateTime? created = row.TryGetValue("CreatedOn", out var c) && c is DateTime d ? d : null;
            if (created is { Kind: DateTimeKind.Utc } && toLocal != null) created = toLocal(created.Value);
            return new InquiryMessageValues(
                Name: FirstWord(Text(row, "Name")),
                Staff: StaffFirstName(staffName, staffEmail),
                Branch: Clean(Text(row, "LocationName")) ?? MessageBranch(message),
                Date: created is null ? null : FormatDate(created.Value, today),
                Source: SourceLabel(Text(row, "InquiryType"), Text(row, "ReferenceName")),
                Workspace: MessageWorkspace(message),
                Seats: MessageSeats(message));
        }

        /// <summary>"9 Oct", or "9 Oct 2025" when not in <paramref name="today"/>'s year.</summary>
        public static string FormatDate(DateTime date, DateTime today) =>
            date.ToString(date.Year == today.Year ? "d MMM" : "d MMM yyyy", CultureInfo.InvariantCulture);

        /// <summary>
        /// How the inquiry came in: Website -> "our website", WhatsApp -> "WhatsApp", WalkIn -> "a walk-in visit",
        /// Reference -> "a reference" (+ " from &lt;name&gt;"); not set / unknown -> null.
        /// </summary>
        public static string? SourceLabel(string? inquiryType, string? referenceName) =>
            InquiryTypes.Normalize(inquiryType) switch
            {
                InquiryTypes.Website => "our website",
                InquiryTypes.WhatsApp => "WhatsApp",
                InquiryTypes.WalkIn => "a walk-in visit",
                InquiryTypes.Reference => Clean(referenceName) is { } who ? $"a reference from {who}" : "a reference",
                _ => null
            };

        /// <summary>The first word of a name ("Ali" of "Ali Raza Khan"), as written; null when empty.</summary>
        public static string? FirstWord(string? name)
        {
            var clean = Clean(name);
            return clean?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        }

        /// <summary>
        /// The staff member's first name: the first word of WN_Users.Name, else a name made from the email the way the
        /// CMS and app do ("sana.iqbal@x" -> "Sana", "ceo1@x" -> "Ceo"); null when neither gives one.
        /// </summary>
        public static string? StaffFirstName(string? name, string? email) => FirstWord(name) ?? EmailPrefixName(email);

        public static string? EmailPrefixName(string? email)
        {
            var prefix = (email ?? "").Trim().Split('@')[0];
            var raw = Regex.Split(prefix, @"[\s._-]+").FirstOrDefault(p => p.Length > 0) ?? "";
            var first = Regex.Replace(raw, @"\d+$", "");
            if (first.Length == 0) first = raw;
            return first.Length == 0 ? null : char.ToUpperInvariant(first[0]) + first[1..];
        }

        private static readonly Regex BranchLabel = new(@"^(?:branch|location)\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WorkspaceLabel = new(@"^(?:workspace(?:\s*type)?|space\s*type)\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SeatsLabel = new(@"^(?:seats?(?:\s*/\s*persons?)?|persons?|people|no\.?\s*of\s*(?:seats|persons|people))\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Number = new(@"\d+", RegexOptions.Compiled);

        /// <summary>"Branch: I-8" (WhatsApp bot, website) / "Location: I-8" (app, older website form) in the message.</summary>
        public static string? MessageBranch(string? message) => Field(message, BranchLabel);

        /// <summary>"Workspace: Private Office" (WhatsApp bot) / a website workspace field, as written.</summary>
        public static string? MessageWorkspace(string? message) => Field(message, WorkspaceLabel);

        /// <summary>"Seats: 3" / "Seats / Persons: 3" -> "3" (a number above 0, else null).</summary>
        public static string? MessageSeats(string? message)
        {
            var value = Field(message, SeatsLabel);
            var m = value == null ? null : Number.Match(value);
            return m is { Success: true } && int.TryParse(m.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0
                ? n.ToString(CultureInfo.InvariantCulture)
                : null;
        }

        /// <summary>
        /// The value of a "Label: value" field of the message: fields are separated by new lines or " | " (the bot writes
        /// "WhatsApp Book a Tour | Branch: I-8 | Workspace: Private Office | Seats / Persons: 6", the website and app one
        /// field per line); a label counts only at the start of a field, never inside free text.
        /// </summary>
        private static string? Field(string? message, Regex label)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;
            foreach (var part in message.Split(new[] { '\n', '\r', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = label.Match(part.Trim());
                if (m.Success && Clean(m.Groups[1].Value) is { } value) return value;
            }
            return null;
        }

        // ---- wa.me -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Phone -> international digits for https://wa.me/&lt;number&gt; (as the app's whatsappNumber): Pakistani local
        /// numbers 03xx-xxxxxxx become 923xxxxxxxxx, a leading 00 is dropped; "" when there are no digits.
        /// </summary>
        public static string WhatsAppNumber(string? phone)
        {
            var digits = new string((phone ?? "").Where(char.IsAsciiDigit).ToArray());
            if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
            if (digits.Length == 11 && digits.StartsWith("03", StringComparison.Ordinal)) return "92" + digits[1..];
            if (digits.Length == 10 && digits.StartsWith('3')) return "92" + digits;
            return digits;
        }

        /// <summary>
        /// <see cref="WhatsAppNumber"/> of a phone that passes the phone rule (<see cref="Phone.IsValid"/>: 11-15 digits,
        /// digits / spaces / dashes / brackets and a leading +); "" otherwise, so text such as "Tour: 2026-06-26 02:" in an
        /// old booking's phone field never becomes a wa.me number.
        /// </summary>
        public static string ValidWhatsAppNumber(string? phone) => Phone.IsValid(phone) ? WhatsAppNumber(phone) : "";

        /// <summary>https://wa.me/&lt;number&gt;?text=&lt;message&gt; (no number: WhatsApp asks whom to send it to).</summary>
        public static string WhatsAppUrl(string? number, string text) =>
            $"https://wa.me/{number ?? ""}?text={Uri.EscapeDataString(text)}";

        // ---- helpers -----------------------------------------------------------------------------------------------

        private static string? Text(IDictionary<string, object?> row, string key) =>
            row.TryGetValue(key, out var v) && v is not null && v is not DBNull ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

        /// <summary>Trimmed, every run of white space (new lines too) one space; null when empty.</summary>
        internal static string? Clean(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var clean = Whitespace.Replace(value, " ").Trim();
            return clean.Length == 0 ? null : clean;
        }
    }

    /// <summary>A placeholder of a message template: its name (without braces) and what it is filled with.</summary>
    public sealed record MessagePlaceholder(string Name, string Description);

    /// <summary>The placeholder values of one inquiry; null / empty = no value.</summary>
    public sealed record InquiryMessageValues(string? Name, string? Staff, string? Branch, string? Date, string? Source,
        string? Workspace, string? Seats)
    {
        public IReadOnlyDictionary<string, string?> ToMap() => new Dictionary<string, string?>
        {
            ["name"] = InquiryMessageRenderer.Clean(Name),
            ["staff"] = InquiryMessageRenderer.Clean(Staff),
            ["branch"] = InquiryMessageRenderer.Clean(Branch),
            ["date"] = InquiryMessageRenderer.Clean(Date),
            ["source"] = InquiryMessageRenderer.Clean(Source),
            ["workspace"] = InquiryMessageRenderer.Clean(Workspace),
            ["seats"] = InquiryMessageRenderer.Clean(Seats),
        };
    }
}
