using WorkNest.Application.DTOs.Customer;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class CustomerService : ICustomerService
    {
        private readonly IDbRepository _db;
        public CustomerService(IDbRepository db) => _db = db;

        public async Task<ApiResponse> GetAllCustomersAsync(int page, int limit, string search)
        {
            var result = await _db.GetAllCustomersAsync(page, limit, search);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> SearchCustomersAsync(string query)
        {
            var result = await _db.SearchCustomersAsync(query);
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> GetCustomerByIdAsync(string id)
        {
            var result = await _db.GetCustomerByGuidAsync(id);
            if (result is null) return ApiResponse.Fail("Customer not found.");
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> CreateCustomerAsync(CustomerRequest request, string? createdBy)
        {
            var result = await _db.CreateCustomerAsync(
                request.FirstName, request.LastName, request.Email,
                request.PhoneNumber, request.CnicOrPassport, request.Address,
                request.CityId, request.Notes, createdBy);

            // Auto-create a user account so the customer can log in with their email
            await _db.SyncUserAsync(request.Email, request.FirstName, request.LastName ?? "", request.PhoneNumber);

            return ApiResponse.Ok(result, "Customer created successfully.");
        }

        public async Task<ApiResponse> UpdateCustomerAsync(string id, CustomerRequest request)
        {
            await _db.UpdateCustomerAsync(id, request.FirstName, request.LastName, request.Email,
                request.PhoneNumber, request.CnicOrPassport, request.Address,
                request.CityId, request.Notes, request.IsActive);
            return ApiResponse.Ok("Customer updated successfully.");
        }

        public async Task<ApiResponse> DeleteCustomerAsync(string id)
        {
            await _db.DeleteCustomerAsync(id);
            return ApiResponse.Ok("Customer deleted.");
        }
    }
}
