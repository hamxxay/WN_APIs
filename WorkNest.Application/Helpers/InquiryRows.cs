namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Tour inquiry list rows (dbo.WN_Contacts_GetList). The list is a UNION of the old website bookings still kept in
    /// dbo.WN_BookTour and dbo.WN_Contacts, and the two tables number their rows independently, so the same Id can
    /// appear twice (one row of each table). Every row therefore gets:
    /// Source ("contact" | "booktour"), Key (unique: "contact-2" / "booktour-2"), CanChangeBranch / CanChangeType
    /// (only rows in WN_Contacts can be changed) and IsNew (the one definition of a new inquiry used by the counts,
    /// see <see cref="IsNewStatus"/>). Writes are addressed by the PublicId, or by Id + Source.
    /// QA_05_staff_sales copies the old bookings into WN_Contacts, after which every row is a "contact" row.
    /// </summary>
    public static class InquiryRows
    {
        public const string Contact = "contact", BookTour = "booktour";

        /// <summary>WN_Contacts_GetList's ContactType for the WN_BookTour rows (before QA_05 added the Source column).</summary>
        private const string BookTourContactType = "Book Tour";

        /// <summary>"contact" / "booktour" for any common spelling ("Contact", "BookTour", "book-tour"); null when unknown or empty.</summary>
        public static string? NormalizeSource(string? value)
        {
            var v = new string((value ?? "").Where(char.IsLetter).ToArray()).ToLowerInvariant();
            return v switch
            {
                "contact" or "contacts" or "inquiry" => Contact,
                "booktour" => BookTour,
                _ => null
            };
        }

        /// <summary>
        /// Which table a row comes from: the procedure's Source column (QA_05), else the ContactType the procedure gives
        /// the WN_BookTour rows ("Book Tour"; WN_Contacts rows are "book_tour" / "contact").
        /// </summary>
        public static string SourceOf(IDictionary<string, object?> row)
        {
            if (row.TryGetValue("Source", out var s) && NormalizeSource(Text(s)) is { } source) return source;
            var type = row.TryGetValue("ContactType", out var t) ? Text(t) : null;
            return string.Equals(type?.Trim(), BookTourContactType, StringComparison.OrdinalIgnoreCase) ? BookTour : Contact;
        }

        public static int IdOf(IDictionary<string, object?> row) =>
            row.TryGetValue("Id", out var v) && v is not null && v is not DBNull && int.TryParse(Convert.ToString(v), out var id) ? id : 0;

        public static string? PublicIdOf(IDictionary<string, object?> row) =>
            row.TryGetValue("PublicId", out var g) && g is not null && g is not DBNull && Guid.TryParse(Convert.ToString(g), out var guid)
                ? guid.ToString() : null;

        /// <summary>The row's unique key in the list: "contact-12" / "booktour-12".</summary>
        public static string KeyOf(string source, int id) => $"{source}-{id}";

        /// <summary>
        /// New = status New. WN_Contacts writes 1; the old website bookings show 0 / 1 / empty as New (the list returns
        /// ISNULL(Status, 1)). The dashboard "New inquiries" figure and the Tour Inquiries badge count the same rows.
        /// </summary>
        public static bool IsNewStatus(object? statusId) =>
            statusId is null || statusId is DBNull || !int.TryParse(Convert.ToString(statusId), out var s) || s <= 1;

        /// <summary>Adds Source, Key, CanChangeBranch, CanChangeType and IsNew to a list row (in place).</summary>
        public static void Annotate(IDictionary<string, object?> row)
        {
            var source = SourceOf(row);
            row["Source"] = source;
            row["Key"] = KeyOf(source, IdOf(row));
            // Branch and Type are columns of WN_Contacts only: the old website bookings can't be changed until QA_05 copies them.
            // InquiryCategory (INQ_01): "tour" | "general"; rows read before INQ_01 are all tour inquiries.
            var category = row.TryGetValue("InquiryCategory", out var c) && c is not null and not DBNull
                           && string.Equals(Convert.ToString(c)?.Trim(), "general", StringComparison.OrdinalIgnoreCase) ? "general" : "tour";
            row["InquiryCategory"] = category;
            row["CanChangeBranch"] = source == Contact;
            // A Simple Inquiry has no inquiry type to change (the CMS / app hide the pencil).
            row["CanChangeType"] = source == Contact && category == "tour";
            row["IsNew"] = IsNewStatus(row.TryGetValue("StatusId", out var st) ? st : null);
        }

        /// <summary>
        /// The visible row an id / public id / source points at. By public id: that row. By id: the row with that Id
        /// (and that Source when given); <c>Ambiguous</c> when two rows (one of each table) have the Id and no source was given.
        /// </summary>
        public static InquiryMatch Find(IEnumerable<IDictionary<string, object?>> rows, Guid? publicId, int? id, string? source)
        {
            if (publicId is { } g)
            {
                var key = g.ToString();
                var byGuid = rows.FirstOrDefault(r => string.Equals(PublicIdOf(r), key, StringComparison.OrdinalIgnoreCase));
                return new InquiryMatch(byGuid, false);
            }
            if (id is not > 0) return new InquiryMatch(null, false);
            var wanted = NormalizeSource(source);
            var matches = rows.Where(r => IdOf(r) == id && (wanted is null || SourceOf(r) == wanted)).ToList();
            return matches.Count switch
            {
                0 => new InquiryMatch(null, false),
                1 => new InquiryMatch(matches[0], false),
                _ => new InquiryMatch(null, true)
            };
        }

        private static string? Text(object? value) => value is null || value is DBNull ? null : Convert.ToString(value);
    }

    /// <summary>A visible inquiry row (null = none); Ambiguous = two rows share the id and the source was not given.</summary>
    public sealed record InquiryMatch(IDictionary<string, object?>? Row, bool Ambiguous)
    {
        public int Id => Row is null ? 0 : InquiryRows.IdOf(Row);
        public string Source => Row is null ? InquiryRows.Contact : InquiryRows.SourceOf(Row);
    }

    public static class InquiryMessages
    {
        public const string NotFound = "Inquiry not found.";
        public const string Ambiguous =
            "Two inquiries have this number (an older website booking and a newer inquiry). Refresh the page and try again.";
        /// <summary>Before QA_05: a write to an old website booking whose number a newer inquiry also has.</summary>
        public const string BookTourNeedsUpdate =
            "This older website booking has the same number as a newer inquiry, so it can't be updated yet. " +
            "It needs the database update QA_05_staff_sales.";
    }
}
