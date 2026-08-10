using WorkNest.Application.DTOs.Payment;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IPaymentService
    {
        Task<(IEnumerable<object> Items, int Total)> GetPaymentsAsync(int page, int limit, string? search);
        Task<IEnumerable<object>> GetMyPaymentsAsync(string userEmail);
        Task<ApiResponse> GetPaymentSummaryAsync(int id);
        Task<ApiResponse> CreatePaymentAsync(PaymentCreateRequest request, string userEmail);
        Task<ApiResponse> GenerateVoucherAsync(VoucherGenerateRequest request, string userEmail);
        Task<ApiResponse> UpdatePaymentStatusAsync(int id, byte statusId, int? actorId);
        Task<ApiResponse> DeletePaymentAsync(int id);
        Task<ApiResponse> ProcessCardPaymentAsync(CardPaymentRequest request, string userEmail);
        Task<ApiResponse> InitiatePayFastAsync(PayFastInitiateRequest request, string userEmail);
        Task<ApiResponse> HandlePayFastNotifyAsync(Dictionary<string, string> formData);
    }
}
