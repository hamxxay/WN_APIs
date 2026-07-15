using WorkNest.Application.DTOs.Customer;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface ICustomerService
    {
        Task<ApiResponse> GetAllCustomersAsync(int page, int limit, string search);
        Task<ApiResponse> SearchCustomersAsync(string query);
        Task<ApiResponse> GetCustomerByIdAsync(string id);
        Task<ApiResponse> CreateCustomerAsync(CustomerRequest request, string? createdBy);
        Task<ApiResponse> UpdateCustomerAsync(string id, CustomerRequest request);
        Task<ApiResponse> DeleteCustomerAsync(string id);
    }
}
