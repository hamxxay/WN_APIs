namespace WorkNest.Application.InvoiceContent
{
    /// <summary>
    /// THE one place that decides which invoice content a PDF is rendered with. Every render path (download, e-mail, approval e-mail,
    /// initial / recurring / custom invoice, delivery retry, sales-tax invoice) asks this and nothing else.
    /// </summary>
    public interface IInvoiceContentResolver
    {
        /// <summary>
        /// The content for an existing invoice. The location is the invoice's own WN_Invoices.LocationId; only when that column is
        /// NULL (the four oldest rows) the location the invoice data already resolved from its space / user is used
        /// (<paramref name="derivedLocationId"/>), never the centre name or any text. The version is the one stamped on the invoice
        /// (WN_Invoices.InvoiceContentVersionId); an invoice that predates the feature uses version 1 of its location and
        /// never changes; a newer one is stamped now with the published version.
        /// </summary>
        Task<ResolvedInvoiceContent> ResolveForInvoiceAsync(int invoiceId, int? derivedLocationId, CancellationToken ct = default);
    }

    public sealed class InvoiceContentResolver : IInvoiceContentResolver
    {
        private readonly IInvoiceContentRepository _repo;
        public InvoiceContentResolver(IInvoiceContentRepository repo) => _repo = repo;

        public async Task<ResolvedInvoiceContent> ResolveForInvoiceAsync(int invoiceId, int? derivedLocationId, CancellationToken ct = default)
        {
            try
            {
                var inv = await _repo.GetInvoiceRefAsync(invoiceId, ct);
                int? locationId = inv?.LocationId ?? derivedLocationId;

                // 1. The version stamped on the invoice: immutable, so the PDF is the same as the first time.
                if (inv?.InvoiceContentVersionId is int stamped)
                {
                    var row = await _repo.GetVersionAsync(stamped, ct);
                    if (row != null) return From(row);
                }

                // 2. Not stamped: an invoice from before the feature is version 1 of its location for good.
                if (inv == null || inv.InvoiceId <= inv.ActivatedFromInvoiceId)
                {
                    var v1 = await VersionOneAsync(locationId, ct);
                    return v1 != null ? From(v1) : BuiltIn(locationId);
                }

                // 3. A newer invoice, first render: the published version of its location, remembered on the invoice from now on.
                var published = await PublishedAsync(locationId, ct);
                if (published == null) return BuiltIn(locationId);
                var finalId = await _repo.StampInvoiceAsync(invoiceId, published.Id, ct);
                if (finalId is int fid && fid != published.Id)
                {
                    var won = await _repo.GetVersionAsync(fid, ct);   // a parallel render stamped it first: use that one
                    if (won != null) return From(won);
                }
                return From(published);
            }
            catch (InvoiceContentMissingException)
            {
                return BuiltIn(derivedLocationId);   // script INVC_01 not run: today's wording
            }
        }

        private async Task<InvoiceContentVersionRow?> PublishedAsync(int? locationId, CancellationToken ct)
        {
            var loc = locationId is > 0 ? locationId.Value : InvoiceContentDefaults.FallbackLocationId;
            return await _repo.GetPublishedAsync(loc, ct)
                ?? (loc != InvoiceContentDefaults.FallbackLocationId ? await _repo.GetPublishedAsync(InvoiceContentDefaults.FallbackLocationId, ct) : null);
        }

        private async Task<InvoiceContentVersionRow?> VersionOneAsync(int? locationId, CancellationToken ct)
        {
            var loc = locationId is > 0 ? locationId.Value : InvoiceContentDefaults.FallbackLocationId;
            return await _repo.GetVersionByNoAsync(loc, 1, ct)
                ?? (loc != InvoiceContentDefaults.FallbackLocationId ? await _repo.GetVersionByNoAsync(InvoiceContentDefaults.FallbackLocationId, 1, ct) : null);
        }

        private static ResolvedInvoiceContent From(InvoiceContentVersionRow row) => new()
        {
            Fields = InvoiceContentFields.FromJson(row.ContentJson),
            LocationId = row.LocationId,
            VersionId = row.Id,
            VersionNo = row.VersionNo,
        };

        /// <summary>Today's hardcoded wording (no tables): not tied to a stored version.</summary>
        public static ResolvedInvoiceContent BuiltIn(int? locationId)
        {
            var loc = locationId is 2 or 3 ? locationId.Value : InvoiceContentDefaults.FallbackLocationId;
            return new ResolvedInvoiceContent { Fields = InvoiceContentDefaults.ForLocation(loc), LocationId = loc };
        }
    }
}
