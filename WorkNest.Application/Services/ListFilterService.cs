using WorkNest.Application.DTOs.Lists;
using WorkNest.Application.DTOs.Quotation;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Admin lists with the LST_01 filters. Business dates come from the business clock; UTC audit columns (payments'
    /// PaidOn / CreatedOn, customers' CreatedAt, see <see cref="DbDateTimeKinds"/>) get UTC bounds so the filter matches
    /// the dates the screens show.
    /// </summary>
    public class ListFilterService : IListFilterService
    {
        private readonly IListFilterRepository _repo;
        private readonly IQuotationService _quotations;
        private readonly IBusinessClock _clock;

        public ListFilterService(IListFilterRepository repo, IQuotationService quotations, IBusinessClock clock)
        {
            _repo = repo;
            _quotations = quotations;
            _clock = clock;
        }

        private ListDateRange Dates(ListFilter f, string column) =>
            ListFilterRules.DateRange(f.From, f.To, DbDateTimeKinds.IsUtcColumn(column), _clock.TimeZone);

        public async Task<(IEnumerable<object> Items, int Total)> GetBookingsAsync(int page, int limit, string? search, int? locationId, ListFilter filter)
        {
            var (rows, total) = await _repo.GetBookingsAsync(page, limit, search, locationId,
                ListFilterRules.BookingStatusIds(filter.Status), Dates(filter, "StartOn"));
            return (rows.Cast<object>(), total);
        }

        public async Task<(IEnumerable<QuotationResponse> Rows, int Total)> GetQuotationsAsync(int page, int limit, string? search, int? locationId, ListFilter filter)
        {
            var (rows, total) = await _repo.GetQuotationsAsync(page, limit, search, locationId,
                ListFilterRules.TextStatus(filter.Status), Dates(filter, "QuotationDate"));
            // Each quotation in full, as GET api/quotation returns it (same as QuotationService.GetQuotationsAsync).
            var list = new List<QuotationResponse>();
            foreach (var r in rows)
            {
                if (!r.TryGetValue("Id", out var id) || id == null) continue;
                var q = await _quotations.GetQuotationByIdAsync(Convert.ToInt32(id));
                if (q != null) list.Add(q);
            }
            return (list, total);
        }

        /// <summary>Rows read per call of the old invoices procedure when LST_01 is missing.</summary>
        public const int InvoiceFallbackChunk = 1000;
        /// <summary>At most this many invoices are read and filtered in the API; more = the database update is needed.</summary>
        public const int InvoiceFallbackMaxRows = 20000;

        public async Task<(IEnumerable<IDictionary<string, object?>> Items, int Total)> GetInvoicesAsync(int page, int limit, string? search, int? typeId, int? locationId, ListFilter filter)
        {
            var status = ListFilterRules.TextStatus(filter.Status);
            var issueDate = Dates(filter, "IssuedOn");
            var today = _clock.Today;
            try
            {
                var (rows, total) = await _repo.GetInvoicesAsync(page, limit, search, typeId, locationId, status, today, issueDate);
                return (rows, total);
            }
            catch (ListFiltersDatabaseUpdateMissingException)
            {
                // dbo.WN_GetInvoicesListFiltered is not on this database yet (LST_01 not run): the Invoices page opens on
                // the current month, so it must not fail. Read the old list (same search / type / location, newest first)
                // and apply the status and issue dates here with the procedure's rules.
                var all = await ReadAllInvoicesUnfilteredAsync(search, typeId, locationId);
                if (all == null) throw;
                return ListFilterRules.PageInvoices(all, status, issueDate, today, page, limit);
            }
        }

        /// <summary>Every invoice of the old list for this search / type / location; null when there are too many to filter here.</summary>
        private async Task<List<IDictionary<string, object?>>?> ReadAllInvoicesUnfilteredAsync(string? search, int? typeId, int? locationId)
        {
            var all = new List<IDictionary<string, object?>>();
            for (int page = 1; ; page++)
            {
                var (rows, total) = await _repo.GetInvoicesUnfilteredAsync(page, InvoiceFallbackChunk, search, typeId, locationId);
                if (total > InvoiceFallbackMaxRows) return null;
                all.AddRange(rows);
                if (rows.Count < InvoiceFallbackChunk || all.Count >= total || all.Count >= InvoiceFallbackMaxRows) return all;
            }
        }

        public async Task<(IEnumerable<object> Items, int Total)> GetPaymentsAsync(int page, int limit, string? search, IReadOnlyCollection<int>? locationIds, ListFilter filter)
        {
            var (rows, total) = await _repo.GetPaymentsAsync(page, limit, search, ListFilterRules.LocationCsv(locationIds),
                ListFilterRules.PaymentStatusId(filter.Status), Dates(filter, "PaidOn"));
            return (rows.Cast<object>(), total);
        }

        public async Task<(IEnumerable<object> Items, int Total)> GetCustomersAsync(int page, int limit, string? search, IReadOnlyCollection<int>? locationIds, ListFilter filter)
        {
            var (rows, total) = await _repo.GetCustomersAsync(page, limit, search, ListFilterRules.LocationCsv(locationIds),
                ListFilterRules.CustomerIsActive(filter.Status), Dates(filter, "CreatedAt"));
            return (rows.Cast<object>(), total);
        }

        public async Task<IReadOnlyList<ListStatusOption>> GetStatusOptionsAsync(string list) =>
            await _repo.GetStatusOptionsAsync(list, _clock.Today);
    }
}
