using WorkNest.Application.DTOs.Lists;
using WorkNest.Application.DTOs.Quotation;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// The new LST_01 list procedures (script LST_01_list_filters): the admin lists with a status, a date range and
    /// (payments, customers) locations. Rows have the same columns as the old list procedures. Every method throws
    /// <see cref="WorkNest.Application.Helpers.ListFiltersDatabaseUpdateMissingException"/> before LST_01 has run.
    /// Date ranges are as the column holds them: Start included, End excluded.
    /// </summary>
    public interface IListFilterRepository
    {
        Task<(List<IDictionary<string, object?>> Rows, int Total)> GetBookingsAsync(int page, int limit, string? search, int? locationId, string? statusIds, ListDateRange startDate);
        Task<(List<IDictionary<string, object?>> Rows, int Total)> GetQuotationsAsync(int page, int limit, string? search, int? locationId, string? status, ListDateRange quotationDate);
        Task<(List<IDictionary<string, object?>> Rows, int Total)> GetInvoicesAsync(int page, int limit, string? search, int? typeId, int? locationId, string? status, DateTime today, ListDateRange issueDate);
        Task<(List<IDictionary<string, object?>> Rows, int Total)> GetPaymentsAsync(int page, int limit, string? search, string? locationIds, int? statusId, ListDateRange paymentDateUtc);
        Task<(List<IDictionary<string, object?>> Rows, int Total)> GetCustomersAsync(int page, int limit, string? search, string? locationIds, bool? isActive, ListDateRange createdUtc);
        Task<List<ListStatusOption>> GetStatusOptionsAsync(string list, DateTime today);
    }

    /// <summary>
    /// Admin lists with the LST_01 filters (used only when a new filter is chosen; otherwise the controllers keep their
    /// old path). Throws <see cref="WorkNest.Application.Helpers.ListFilterException"/> for a wrong filter value.
    /// </summary>
    public interface IListFilterService
    {
        Task<(IEnumerable<object> Items, int Total)> GetBookingsAsync(int page, int limit, string? search, int? locationId, ListFilter filter);
        Task<(IEnumerable<QuotationResponse> Rows, int Total)> GetQuotationsAsync(int page, int limit, string? search, int? locationId, ListFilter filter);
        Task<(IEnumerable<IDictionary<string, object?>> Items, int Total)> GetInvoicesAsync(int page, int limit, string? search, int? typeId, int? locationId, ListFilter filter);
        /// <summary><paramref name="locationIds"/>: null = every location.</summary>
        Task<(IEnumerable<object> Items, int Total)> GetPaymentsAsync(int page, int limit, string? search, IReadOnlyCollection<int>? locationIds, ListFilter filter);
        /// <summary><paramref name="locationIds"/>: null = every customer.</summary>
        Task<(IEnumerable<object> Items, int Total)> GetCustomersAsync(int page, int limit, string? search, IReadOnlyCollection<int>? locationIds, ListFilter filter);
        /// <summary>The Status filter choices of a list (bookings, quotations, invoices, payments, customers).</summary>
        Task<IReadOnlyList<ListStatusOption>> GetStatusOptionsAsync(string list);
    }
}
