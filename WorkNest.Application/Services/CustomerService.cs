using WorkNest.Application.Helpers;
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

        private static string? ResolveCompany(string? customerType, string? company)
        {
            if (!string.IsNullOrWhiteSpace(company))
            {
                if (string.Equals(customerType, "Individual", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(company.Trim(), "Individual", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return company.Trim();
            }

            if (string.Equals(customerType, "Company", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(customerType, "Business", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(customerType, "AOP", StringComparison.OrdinalIgnoreCase))
            {
                return "Company";
            }

            return null;
        }

        private static string? ValidateCompanyNtn(CustomerRequest request)
        {
            var isCompany = string.Equals(request.CustomerType, "Company", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(request.CustomerType, "AOP", StringComparison.OrdinalIgnoreCase);
            if (!isCompany) return null;

            if (string.IsNullOrWhiteSpace(request.Company))
                return "Company / Business Name is required for company customers.";

            if (string.IsNullOrWhiteSpace(request.Ntn))
                return "NTN (National Tax Number) is required when Entity Type is Company.";

            var cleanNtn = request.Ntn.Trim().Replace("-", "");
            if (cleanNtn.Length != 8 || !cleanNtn.All(char.IsDigit))
                return "Please enter a valid 8-digit NTN (e.g. 1234567-8).";

            return null;
        }

        public async Task<ApiResponse> CreateCustomerAsync(CustomerRequest request, string? createdBy)
        {
            var ntnErr = ValidateCompanyNtn(request);
            if (ntnErr is not null)
                return ApiResponse.Fail(ntnErr);
            var cnicErr = Cnic.Validate(request.CnicOrPassport);
            if (cnicErr is not null)
                return ApiResponse.Fail(cnicErr);

            var existing = await _db.GetCustomerByEmailAsync(request.Email);
            if (existing is not null && existing.TryGetValue("Id", out var eid) && eid is not null)
                return ApiResponse.Fail("A customer with this email already exists.");

            var (userId, _) = await _db.SyncUserAsync(request.Email,
                $"{request.FirstName} {request.LastName}".Trim(),
                request.PhoneNumber);

            string? companyValue = ResolveCompany(request.CustomerType, request.Company);

            IDictionary<string, object?> result;
            try
            {
                result = await _db.CreateCustomerAsync(
                    request.FirstName, request.LastName, request.Email,
                    request.PhoneNumber, request.CnicOrPassport, request.Address,
                    request.CityId, request.Notes, createdBy, userId, companyValue,
                    request.Ntn, request.SecpRegistrationNo);
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
            var ntnErr = ValidateCompanyNtn(request);
            if (ntnErr is not null)
                return ApiResponse.Fail(ntnErr);
            var cnicErr = Cnic.Validate(request.CnicOrPassport);
            if (cnicErr is not null)
                return ApiResponse.Fail(cnicErr);

            string? companyValue = ResolveCompany(request.CustomerType, request.Company);

            var updated = await _db.UpdateCustomerAsync(id, request.FirstName, request.LastName, request.Email,
                request.PhoneNumber, request.CnicOrPassport, request.Address,
                request.CityId, request.Notes, request.IsActive, companyValue,
                request.Ntn, request.SecpRegistrationNo);
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
