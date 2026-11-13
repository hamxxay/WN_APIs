using System.Text.Json;

namespace WorkNest.Application.InvoiceContent
{
    public sealed record InvoiceContentLocationSummary(int LocationId, string LocationName, int? PublishedVersionNo, string? PublishedBy, DateTime? PublishedAt, bool HasDraft, bool HasPlaceholders);
    public sealed record InvoiceContentPublishedView(int VersionNo, string? PublishedBy, DateTime? PublishedAt, Dictionary<string, object> Content);
    public sealed record InvoiceContentDraftView(Dictionary<string, object> Content, string? UpdatedBy, DateTime? UpdatedAt);
    public sealed record InvoiceContentVersionSummary(int VersionNo, string Status, string? PublishedBy, DateTime? PublishedAt);
    public sealed record InvoiceContentLocationView(int LocationId, string LocationName, InvoiceContentPublishedView? Published, InvoiceContentDraftView? Draft,
        IReadOnlyList<InvoiceContentVersionSummary> Versions, IReadOnlyList<string> Placeholders);

    /// <summary>The request was not valid (400): the messages are shown to the staff member.</summary>
    public sealed class InvoiceContentValidationException : Exception
    {
        public IReadOnlyList<string> Errors { get; }
        public InvoiceContentValidationException(string message, IReadOnlyList<string> errors) : base(message) => Errors = errors;
    }

    /// <summary>The location is not one of the invoice-content locations (404).</summary>
    public sealed class InvoiceContentLocationNotFoundException : Exception
    {
        public InvoiceContentLocationNotFoundException() : base("Location not found.") { }
    }

    /// <summary>Reading, drafting, publishing and previewing the invoice content of a location (admin pages).</summary>
    public sealed class InvoiceContentService
    {
        private readonly IInvoiceContentRepository _repo;
        public InvoiceContentService(IInvoiceContentRepository repo) => _repo = repo;

        public async Task<IReadOnlyList<InvoiceContentLocationSummary>> GetLocationsAsync(IReadOnlyCollection<int>? allowedLocationIds, CancellationToken ct = default)
        {
            var locations = await _repo.GetLocationsAsync(ct);
            var list = new List<InvoiceContentLocationSummary>();
            foreach (var l in locations.Where(l => allowedLocationIds == null || allowedLocationIds.Contains(l.LocationId)).OrderBy(l => l.LocationId))
            {
                var rows = l.HasContent ? await _repo.GetVersionsAsync(l.LocationId, ct) : Array.Empty<InvoiceContentVersionRow>();
                var pub = rows.FirstOrDefault(r => r.Status == InvoiceContentStatus.Published);
                list.Add(new InvoiceContentLocationSummary(l.LocationId, NameOf(l), pub?.VersionNo, pub?.PublishedBy, pub?.PublishedAt,
                    rows.Any(r => r.Status == InvoiceContentStatus.Draft),
                    pub != null && InvoiceContentFields.FromJson(pub.ContentJson).PlaceholderKeys().Count > 0));
            }
            return list;
        }

        public async Task<InvoiceContentLocationView> GetAsync(int locationId, CancellationToken ct = default)
        {
            var loc = await RequireLocationAsync(locationId, ct);
            var rows = await _repo.GetVersionsAsync(locationId, ct);
            if (rows.Count == 0)
            {
                // A location added after the script ran: version 1 = what it prints today, so nothing already issued changes.
                await _repo.EnsureSeedAsync(locationId, InvoiceContentDefaults.ForLocation(locationId).ToJson(), "invoice-content", ct);
                rows = await _repo.GetVersionsAsync(locationId, ct);
            }
            return View(loc, rows);
        }

        public async Task<InvoiceContentLocationView> SaveDraftAsync(int locationId, JsonElement? content, string? by, CancellationToken ct = default)
        {
            await GetAsync(locationId, ct);   // also seeds version 1 when missing
            var v = InvoiceContentValidator.Validate(content);
            if (!v.IsValid) throw new InvoiceContentValidationException("The invoice content has errors. Fix them and save again.", v.Errors);
            await _repo.SaveDraftAsync(locationId, v.Content!.ToJson(), by, ct);
            return await GetAsync(locationId, ct);
        }

        public async Task<InvoiceContentLocationView> DiscardDraftAsync(int locationId, CancellationToken ct = default)
        {
            await GetAsync(locationId, ct);
            await _repo.DeleteDraftAsync(locationId, ct);
            return await GetAsync(locationId, ct);
        }

        public async Task<InvoiceContentLocationView> PublishAsync(int locationId, string? by, CancellationToken ct = default)
        {
            await GetAsync(locationId, ct);
            var draft = (await _repo.GetVersionsAsync(locationId, ct)).FirstOrDefault(r => r.Status == InvoiceContentStatus.Draft);
            if (draft == null) throw new InvoiceContentValidationException("There is no draft to publish. Save a draft first.", new[] { "No draft saved for this location." });
            // Re-validated at publish: a draft is only ever published as valid content.
            var v = InvoiceContentValidator.Validate(InvoiceContentFields.FromJson(draft.ContentJson));
            if (!v.IsValid) throw new InvoiceContentValidationException("The draft has errors and was not published.", v.Errors);
            var published = await _repo.PublishDraftAsync(locationId, by, ct);
            if (published == null) throw new InvoiceContentConflictException("The draft was already published or discarded. Reload the page.");
            return await GetAsync(locationId, ct);
        }

        /// <summary>The content to preview: the submitted one (validated), else the draft, else the published version.</summary>
        public async Task<InvoiceContentFields> GetPreviewContentAsync(int locationId, JsonElement? content, CancellationToken ct = default)
        {
            await GetAsync(locationId, ct);
            if (content is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined })
            {
                var v = InvoiceContentValidator.Validate(content);
                if (!v.IsValid) throw new InvoiceContentValidationException("The invoice content has errors, so it cannot be previewed.", v.Errors);
                return v.Content!;
            }
            var rows = await _repo.GetVersionsAsync(locationId, ct);
            var row = rows.FirstOrDefault(r => r.Status == InvoiceContentStatus.Draft) ?? rows.First(r => r.Status == InvoiceContentStatus.Published);
            return InvoiceContentFields.FromJson(row.ContentJson);
        }

        private async Task<InvoiceContentLocationRow> RequireLocationAsync(int locationId, CancellationToken ct)
        {
            var loc = locationId > 0 ? (await _repo.GetLocationsAsync(ct)).FirstOrDefault(l => l.LocationId == locationId) : null;
            return loc ?? throw new InvoiceContentLocationNotFoundException();
        }

        private static string NameOf(InvoiceContentLocationRow l) => string.IsNullOrWhiteSpace(l.LocationName) ? $"Location {l.LocationId}" : l.LocationName!;

        private static InvoiceContentLocationView View(InvoiceContentLocationRow loc, IReadOnlyList<InvoiceContentVersionRow> rows)
        {
            var pub = rows.FirstOrDefault(r => r.Status == InvoiceContentStatus.Published);
            var draft = rows.FirstOrDefault(r => r.Status == InvoiceContentStatus.Draft);
            var shown = draft ?? pub;
            return new InvoiceContentLocationView(
                loc.LocationId, NameOf(loc),
                pub == null ? null : new InvoiceContentPublishedView(pub.VersionNo ?? 0, pub.PublishedBy, pub.PublishedAt, InvoiceContentFields.FromJson(pub.ContentJson).ToApi()),
                draft == null ? null : new InvoiceContentDraftView(InvoiceContentFields.FromJson(draft.ContentJson).ToApi(), draft.UpdatedBy ?? draft.CreatedBy, draft.UpdatedAt ?? draft.CreatedAt),
                rows.Where(r => r.VersionNo != null).OrderByDescending(r => r.VersionNo)
                    .Select(r => new InvoiceContentVersionSummary(r.VersionNo!.Value, r.Status, r.PublishedBy, r.PublishedAt)).ToList(),
                shown == null ? new List<string>() : InvoiceContentFields.FromJson(shown.ContentJson).PlaceholderKeys());
        }
    }
}
