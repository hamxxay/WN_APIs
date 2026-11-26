using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.DayPass;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Result with an HTTP status: 200 / 201 data, 400 rule broken, 403 other location, 404 not found, 409 no longer available, 503 not installed.</summary>
    public class DayPassResult<T>
    {
        public int StatusCode { get; init; } = 200;
        public string Message { get; init; } = "Success";
        public T? Data { get; init; }
        public bool Ok => StatusCode is >= 200 and < 300;
        public static DayPassResult<T> Success(T data, string message = "Success", int status = 200) => new() { StatusCode = status, Data = data, Message = message };
        public static DayPassResult<T> Fail(int status, string message) => new() { StatusCode = status, Message = message };
    }

    public interface IDayPassService
    {
        Task<bool> IsInstalledAsync();
        Task<DayPassResult<List<DayPassSpace>>> AvailableSpacesAsync(string? date, string? window, int? locationId, IReadOnlyCollection<int>? allowedLocationIds);
        Task<DayPassResult<DayPassQuote>> QuoteAsync(DayPassRequest? request, IReadOnlyCollection<int>? allowedLocationIds);
        Task<DayPassResult<DayPassDto>> CreateAsync(DayPassRequest? request, string? actorEmail, IReadOnlyCollection<int>? allowedLocationIds);
        Task<DayPassResult<DayPassPage>> ListAsync(DayPassListFilter filter, IReadOnlyCollection<int>? allowedLocationIds);
        Task<DayPassResult<DayPassDto>> GetAsync(int id, IReadOnlyCollection<int>? allowedLocationIds);
        Task<DayPassResult<DayPassDto>> CancelAsync(int id, string? reason, string? actorEmail, IReadOnlyCollection<int>? allowedLocationIds);
        Task<DayPassResult<object>> EmailReceiptAsync(int id, string? actorEmail, IReadOnlyCollection<int>? allowedLocationIds);
        Task<DayPassResult<byte[]>> ReceiptPdfAsync(int id, IReadOnlyCollection<int>? allowedLocationIds);
        Task<DayPassResult<List<DayPassCustomerRow>>> CustomersAsync(string? search, IReadOnlyCollection<int>? allowedLocationIds);
        /// <summary>Takes the door access of every person on the pass away (ended or cancelled pass). True when the machines took it.</summary>
        Task<bool> RemoveAccessAsync(DayPassAccessDue due);
        Task<List<DayPassAccessDue>> AccessDueAsync();
    }

    public interface IDayPassReceiptPdf
    {
        byte[] Generate(WorkNest.Application.DTOs.DayPass.DayPassDto pass);
    }
}
