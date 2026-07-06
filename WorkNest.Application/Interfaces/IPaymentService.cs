using WorkNest.Application.DTOs.Payment;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Payment processing and gateway operations.</summary>
    public interface IPaymentService
    {
        Task<IEnumerable<object>> GetAllPaymentsAsync();
        Task<IEnumerable<object>> GetMyPaymentsAsync(string userEmail);
        Task<ApiResponse> GetPaymentSummaryAsync(string id);
        Task<ApiResponse> CreatePaymentAsync(PaymentCreateRequest request, string userEmail);
        Task<ApiResponse> UpdatePaymentStatusAsync(string id, string status, string? transactionRef);
        Task<ApiResponse> DeletePaymentAsync(string id);
        Task<ApiResponse> ProcessCardPaymentAsync(CardPaymentRequest request, string userEmail);
        Task<ApiResponse> GenerateVoucherAsync(VoucherGenerateRequest request, string userEmail);
        Task<ApiResponse> InitiatePayFastAsync(PayFastInitiateRequest request, string userEmail);
        Task<ApiResponse> HandlePayFastNotifyAsync(Dictionary<string, string> formData);
    }
}
