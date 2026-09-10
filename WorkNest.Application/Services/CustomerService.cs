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
            var existing = await _db.GetCustomerByEmailAsync(request.Email);
            if (existing is not null && existing.TryGetValue("Id", out var eid) && eid is not null)
                return ApiResponse.Fail("A customer with this email already exists.");

            var (userId, _) = await _db.SyncUserAsync(request.Email,
                $"{request.FirstName} {request.LastName}".Trim(),
                request.PhoneNumber);

            IDictionary<string, object?> result;
            try
            {
                result = await _db.CreateCustomerAsync(
                    request.FirstName, request.LastName, request.Email,
                    request.PhoneNumber, request.CnicOrPassport, request.Address,
                    request.CityId, request.Notes, createdBy, userId, request.Company);
            }
            catch (Exception ex) when (ex.Message.Contains("UQ_WN_Customers") || ex.Message.Contains("duplicate key"))
            {
                return ApiResponse.Fail("A customer with this email already exists.");
            }

            if (!result.TryGetValue("Id", out var custIdObj) || custIdObj is null)
                return ApiResponse.Fail("Customer could not be created. The stored procedure returned no result.");

            return ApiResponse.Ok(result, "Customer created successfully.");
        }

        public async Task<ApiResponse> UpdateCustomerAsync(string id, CustomerRequest request)
        {
            var updated = await _db.UpdateCustomerAsync(id, request.FirstName, request.LastName, request.Email,
                request.PhoneNumber, request.CnicOrPassport, request.Address,
                request.CityId, request.Notes, request.IsActive, request.Company);
            if (updated is null)
                return ApiResponse.Fail("Customer not found or update failed.");
            return ApiResponse.Ok(updated, "Customer updated successfully.");
        }

        public async Task<ApiResponse> DeleteCustomerAsync(string id)
        {
            await _db.DeleteCustomerAsync(id);
            return ApiResponse.Ok("Customer deleted.");
        }
    }
}
