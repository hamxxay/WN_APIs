using WorkNest.Application.Helpers;
using WorkNest.Application.DTOs.Customer;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class CustomerService : ICustomerService
    {
        /// <summary>Shown by the customer detail page (in the affected sections) until CD_01 has run.</summary>
        public const string OverviewDbUpdateMessage = "Database update needed: run the script CD_01_customer_detail_TEST";

        /// <summary>Rows per list on the customer detail page (bookings, receipts, payments, agreements, deposits, timeline).</summary>
        private const int OverviewRows = 100;

        private readonly IDbRepository _db;
        private readonly IOrderStatusService _orderStatus;
        private readonly IBusinessClock _clock;

        public CustomerService(IDbRepository db, IOrderStatusService orderStatus, IBusinessClock clock)
        {
            _db = db;
            _orderStatus = orderStatus;
            _clock = clock;
        }

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

        public async Task<CustomerOverviewResult> GetCustomerOverviewAsync(int id)
        {
            if (id <= 0) return new CustomerOverviewResult(404, ApiResponse.Fail("Customer not found."));

            // Same invoice status sets as the CEO dashboard: open = Unpaid / Partial / Challan Expire; void = Cancelled + legacy 5.
            var st = await _orderStatus.GetInvoiceStatusesAsync();
            var open = st.Unpaid.Concat(st.Partial).Concat(st.Overdue).Distinct().ToList();
            var paid = st.Paid.ToList();
            var voided = st.Cancelled.Append(5).Distinct().ToList();

            var sets = await _db.GetCustomerOverviewAsync(id, _clock.Now, open, paid, voided, OverviewRows);
            if (sets is null) return new CustomerOverviewResult(503, ApiResponse.Fail(OverviewDbUpdateMessage));

            List<IDictionary<string, object?>> Set(int i) => i < sets.Count ? sets[i] : new List<IDictionary<string, object?>>();
            var summary = Set(0).FirstOrDefault();
            if (summary is null) return new CustomerOverviewResult(404, ApiResponse.Fail("Customer not found."));

            return new CustomerOverviewResult(200, ApiResponse.Ok(new
            {
                summary,
                bookings = Set(1),
                receipts = Set(2),
                payments = Set(3),
                agreements = Set(4),
                deposits = Set(5),
                timeline = Set(6)
            }));
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

        private static bool IsCompanyCustomer(string? customerType) =>
            string.Equals(customerType, "Company", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(customerType, "Business", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(customerType, "AOP", StringComparison.OrdinalIgnoreCase);

        private static string? ValidateCompanyNtn(CustomerRequest request)
        {
            if (!IsCompanyCustomer(request.CustomerType)) return null;

            if (string.IsNullOrWhiteSpace(request.Company))
                return "Company / Business Name is required for company customers.";

            if (string.IsNullOrWhiteSpace(request.Ntn))
                return "NTN (National Tax Number) is required when Entity Type is Company.";

            var cleanNtn = request.Ntn.Trim();
            if (cleanNtn.Length > 50)
                return "NTN cannot exceed 50 characters.";

            return null;
        }

        public async Task<ApiResponse> CreateCustomerAsync(CustomerRequest request, string? createdBy)
        {
            var ntnErr = ValidateCompanyNtn(request);
            if (ntnErr is not null)
                return ApiResponse.Fail(ntnErr);

            var isCompany = IsCompanyCustomer(request.CustomerType);
            if (!isCompany)
            {
                var cnicErr = Cnic.Validate(request.CnicOrPassport);
                if (cnicErr is not null)
                    return ApiResponse.Fail(cnicErr);
            }

            var phoneErr = Phone.Validate(request.PhoneNumber);
            if (phoneErr is not null)
                return ApiResponse.Fail(phoneErr);

            var existing = await _db.GetCustomerByEmailAsync(request.Email);
            if (existing is not null && existing.TryGetValue("Id", out var eid) && eid is not null)
                return ApiResponse.Fail("A customer with this email already exists.");

            var (userId, _) = await _db.SyncUserAsync(request.Email,
                PersonName.Join(request.FirstName, request.LastName),
                request.PhoneNumber);

            string? companyValue = ResolveCompany(request.CustomerType, request.Company);

            IDictionary<string, object?> result;
            try
            {
                result = await _db.CreateCustomerAsync(
                    request.FirstName, request.LastName, request.Email,
                    request.PhoneNumber, request.CnicOrPassport, request.Address,
                    request.CityId, request.Notes, createdBy, userId, companyValue,
                    request.CustomerTypeId, request.CountryId, request.IdentificationDocumentTypeId,
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

            var isCompany = IsCompanyCustomer(request.CustomerType);
            if (!isCompany)
            {
                var cnicErr = Cnic.Validate(request.CnicOrPassport);
                if (cnicErr is not null)
                    return ApiResponse.Fail(cnicErr);
            }

            var phoneErr = Phone.Validate(request.PhoneNumber);
            if (phoneErr is not null)
                return ApiResponse.Fail(phoneErr);

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
