namespace WorkNest.Application.InvoiceContent
{
    /// <summary>Thrown when the tables / column of script INVC_01 are not on the database (SQL error 207 / 208 / 2812).</summary>
    public sealed class InvoiceContentMissingException : Exception
    {
        public const string UserMessage = "Invoice content is not set up on this server yet.";
        public InvoiceContentMissingException(Exception? inner = null) : base(UserMessage, inner) { }
    }

    /// <summary>Another request changed the same content meanwhile.</summary>
    public sealed class InvoiceContentConflictException : Exception
    {
        public InvoiceContentConflictException(string message) : base(message) { }
    }

    public static class InvoiceContentStatus
    {
        public const string Draft = "Draft";
        public const string Published = "Published";
        public const string Superseded = "Superseded";
    }

    public sealed record InvoiceContentVersionRow(
        int Id, int LocationId, int? VersionNo, string Status, string ContentJson,
        DateTime CreatedAt, string? CreatedBy, DateTime? UpdatedAt, string? UpdatedBy, DateTime? PublishedAt, string? PublishedBy);

    public sealed record InvoiceContentLocationRow(int LocationId, string? LocationName, bool HasContent);

    /// <summary>What the resolver needs of an invoice: its own WN_Invoices.LocationId and stamp, and where the feature started.</summary>
    public sealed record InvoiceContentInvoiceRef(int InvoiceId, int? LocationId, int? InvoiceContentVersionId, int ActivatedFromInvoiceId);

    /// <summary>
    /// Tables of script INVC_01_location_invoice_content: dbo.WN_InvoiceContentVersions, dbo.WN_InvoiceContentSettings and the column
    /// dbo.WN_Invoices.InvoiceContentVersionId. Every member throws <see cref="InvoiceContentMissingException"/> before the script is run.
    /// </summary>
    public interface IInvoiceContentRepository
    {
        /// <summary>Locations the settings page covers: the known ones (1, 2, 3) and every WN_Locations row, with whether they have content yet.</summary>
        Task<IReadOnlyList<InvoiceContentLocationRow>> GetLocationsAsync(CancellationToken ct = default);
        /// <summary>The published, superseded and draft rows of a location (content included).</summary>
        Task<IReadOnlyList<InvoiceContentVersionRow>> GetVersionsAsync(int locationId, CancellationToken ct = default);
        Task<InvoiceContentVersionRow?> GetVersionAsync(int versionId, CancellationToken ct = default);
        Task<InvoiceContentVersionRow?> GetVersionByNoAsync(int locationId, int versionNo, CancellationToken ct = default);
        Task<InvoiceContentVersionRow?> GetPublishedAsync(int locationId, CancellationToken ct = default);
        /// <summary>Inserts version 1 (published) when the location has no rows at all. Returns false when it already had.</summary>
        Task<bool> EnsureSeedAsync(int locationId, string contentJson, string? by, CancellationToken ct = default);
        /// <summary>Creates or replaces the location's one draft.</summary>
        Task<InvoiceContentVersionRow> SaveDraftAsync(int locationId, string contentJson, string? by, CancellationToken ct = default);
        Task<bool> DeleteDraftAsync(int locationId, CancellationToken ct = default);
        /// <summary>Turns the draft into the next published version (the previous one becomes Superseded), in one transaction. Null when there is no draft.</summary>
        Task<InvoiceContentVersionRow?> PublishDraftAsync(int locationId, string? by, CancellationToken ct = default);
        Task<InvoiceContentInvoiceRef?> GetInvoiceRefAsync(int invoiceId, CancellationToken ct = default);
        /// <summary>Stamps the invoice with the version only while it has none; returns the version id the invoice ends up with.</summary>
        Task<int?> StampInvoiceAsync(int invoiceId, int versionId, CancellationToken ct = default);
    }
}
