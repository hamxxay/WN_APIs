using WorkNest.Application.DTOs.SpaceConfig;

namespace WorkNest.Application.Interfaces
{
    public interface IDbRepository
    {
        // --- User ---
        Task<(int? Id, string? PublicId)> SyncUserAsync(string email, string? name, string? phone, string? passwordHash = null, int? roleId = null, int? companyId = null);
        Task<(int? Id, string? PublicId)> GetUserIdByEmailAsync(string email);
        Task<IDictionary<string, object?>?> GetUserByEmailAsync(string email);
        Task<IDictionary<string, object?>?> GetUserByIdAsync(int id);
        Task<IDictionary<string, object?>?> GetUserByPublicIdAsync(Guid publicId);
        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetUsersAsync(int page, int limit, string? search);
        Task<IEnumerable<IDictionary<string, object?>>> GetUserHistoryAsync(int userId);
        Task<(int? Id, string? PublicId)> CreateUserAsync(string email, string? passwordHash, string? name, string? phone, int? roleId, int? companyId, int? cityId, string? address, string? cnic, string? avatarUrl, string? notes, int? createdById);
        Task UpdateUserAsync(int id, string? name, string? phone, int? companyId, int? cityId, string? address, string? cnic, string? avatarUrl, string? notes);
        Task DeleteUserAsync(int id);
        Task SetUserStatusAsync(int id, bool isActive);
        Task SetUserRoleAsync(int id, int roleId);

        // --- Space ---
        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetSpacesAsync(int page, int limit, string? search);
        Task<IDictionary<string, object?>?> GetSpaceSummaryAsync(int id);
        Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesAsync();
        Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesByTypeAsync(int spaceTypeId, DateTime startOn, DateTime endOn);
        Task<IEnumerable<IDictionary<string, object?>>> GetAvailabilityCountsAsync();
        Task<(int? Id, string? PublicId)> InsertSpaceAsync(string name, int locationId, int spaceTypeId, string? code, string? description, int? floorId, string? imageUrl, int capacity, int? createdById, decimal? price = null, byte? billingPeriodId = null);
        Task UpdateSpaceAsync(int id, string? name, int? locationId, int? spaceTypeId, string? code, string? description, int? floorId, string? imageUrl, int? capacity, int? updatedById, decimal? price = null, byte? billingPeriodId = null);
        Task<IEnumerable<IDictionary<string, object?>>> GetBillingPeriodsAsync();
        Task DeleteSpaceAsync(int id);

        // --- Booking ---
        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetBookingsAsync(int page, int limit, string? search);
        Task<IDictionary<string, object?>?> GetBookingByPublicIdAsync(Guid publicId, string? userEmail = null);
        Task<IEnumerable<IDictionary<string, object?>>> GetMyBookingsAsync(string userEmail);
        Task<IEnumerable<IDictionary<string, object?>>> GetRecentBookingsAsync(int top = 10);
        Task<IEnumerable<IDictionary<string, object?>>> GetBookingCalendarAsync(int spaceId, int year, int month);
        Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesForBookingAsync(int spaceTypeId, DateTime startOn, DateTime endOn, int? capacity = null);
        Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesForReassignmentAsync(int spaceTypeId, DateTime startOn, DateTime endOn, int excludeBookingId);
        Task<IEnumerable<IDictionary<string, object?>>> GetSmartAvailableSpacesAsync(string categoryCode, DateTime startOn, DateTime endOn, int? capacity = null);
        Task<IDictionary<string, object?>> InsertBookingAsync(int userId, int spaceId, int pricingId, DateTime startOn, DateTime endOn, string? notes, int? createdById, string? userEmail, string? customerEmail = null, string? customerFirstName = null, string? customerLastName = null, string? customerPhone = null, string? customerCnic = null, string? customerAddress = null, int? customerCityId = null, string? customerNotes = null, decimal discountPercentage = 0, string discountType = "Percentage", decimal discountValue = 0, decimal? securityDepositOverride = null, int? floorId = null, int? billingPeriodMonths = null, int? securityDepositMonths = null, int? advanceRentMonths = null);
        Task<IDictionary<string, object?>> InsertSmartBookingAsync(string userEmail, string categoryCode, DateTime startOn, DateTime endOn, int? capacity, string? notes, int? createdById, string? customerEmail = null, string? customerFirstName = null, string? customerLastName = null, string? customerPhone = null, string? customerCnic = null, string? customerAddress = null, int? customerCityId = null, string? customerNotes = null);
        Task UpdateBookingAsync(int id, DateTime? startOn, DateTime? endOn, string? notes, int? updatedById);
        Task UpdateBookingStatusAsync(int id, byte statusId, int? updatedById);
        Task CancelBookingAsync(int id, string? userEmail, string? cancelReason, int? updatedById);
        Task ReassignBookingAsync(int id, int newSpaceId, int newPricingId, string? userEmail, int? updatedById);

        // --- Quotation ---
        Task<IDictionary<string, object?>> InsertQuotationAsync(
            string quotationNumber,
            DateTime validUntil,
            int? customerId,
            int? spaceId,
            DateTime startDateTime,
            DateTime endDateTime,
            decimal subtotalAmount,
            decimal discountPercentage,
            string? remarks = null,
            int? createdById = null,
            string discountType = "Percentage",
            decimal discountValue = 0,
            decimal? securityDepositOverride = null,
            int? floorId = null,
            int? billingPeriodMonths = null,
            decimal? perSeatBasePrice = null,
            int? capacity = null,
            decimal? monthlyBasePrice = null,
            decimal? maxDiscountPercent = null,
            int? securityDepositMonths = null,
            decimal? securityDeposit = null
        );
        Task<IDictionary<string, object?>?> GetQuotationByIdAsync(
            int quotationId,
            string? userEmail = null
        );

        Task<IEnumerable<IDictionary<string, object?>>> GetQuotationDetailsAsync(
            int quotationId
        );

        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetQuotationsAsync(
            int page,
            int limit,
            string? search
        );

        Task<IEnumerable<IDictionary<string, object?>>> GetQuotationHistoryAsync(
            int quotationId,
            string? userEmail = null
        );
        Task<IEnumerable<IDictionary<string, object?>>> GetQuotationsByCustomerAsync(int customerId);
        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetCustomerQuotationsAsync(int customerId, int page, int limit, string? status);
        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetCustomerInvoicesDbAsync(int customerId, int userId, int page, int limit, int? statusId);
        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetCustomerAttendantsPaginatedDbAsync(int customerId, int page, int limit);
        Task<bool> CheckBookingOwnershipAsync(int bookingId, int customerId, int userId);
        Task<IDictionary<string, object?>> ConvertQuotationToBookingAsync(
            int quotationId,
            int? createdById
        );
        Task<IDictionary<string, object?>> AcceptQuotationAsync(int quotationId, int version, int customerId, string? note, int? userId);
        Task<IDictionary<string, object?>> DeclineQuotationAsync(int quotationId, int version, int customerId, string note, int? userId);
        Task<IDictionary<string, object?>> CreateQuotationNewVersionAsync(int quotationId, int? createdById);
        Task<IEnumerable<IDictionary<string, object?>>> GetQuotationVersionsAsync(int quotationId);
        Task<IEnumerable<IDictionary<string, object?>>> GetQuotationActivitiesAsync(int? quotationId, int limit);
        Task SendQuotationStatusAsync(int quotationId, string status, int? userId);
        // --- Payment ---
        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetPaymentsAsync(int page, int limit, string? search);
        Task<IDictionary<string, object?>?> GetPaymentSummaryAsync(int id);
        Task<IEnumerable<IDictionary<string, object?>>> GetMyPaymentsAsync(string userEmail);
        Task<IDictionary<string, object?>> InsertPaymentAsync(int userId, int? bookingId, byte paymentMethodId, decimal amount, string? notes, int? createdById);
        Task<IDictionary<string, object?>> GenerateVoucherAsync(int userId, int? bookingId, decimal amount, DateTime expiresOn, int? createdById);
        Task UpdatePaymentStatusAsync(int id, byte statusId, int? updatedById);
        Task DeletePaymentAsync(int id);

        // --- Membership ---
        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetMembershipsAsync(int page, int limit, string? search);
        Task<IDictionary<string, object?>?> GetMembershipSummaryAsync(int id);
        Task<IDictionary<string, object?>> InsertMembershipAsync(int userId, int planId, DateTime startOn, DateTime? endOn, bool autoRenew, string? notes, int? createdById);
        Task UpdateMembershipStatusAsync(int id, byte statusId, int? updatedById);
        Task DeleteMembershipAsync(int id);

        // --- PricingPlan ---
        Task<IEnumerable<IDictionary<string, object?>>> GetAllPricingPlansAsync();
        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetPricingPlansAsync(int page, int limit);
        Task<IDictionary<string, object?>?> GetPricingPlanSummaryAsync(int id);
        Task<(int? Id, string? PublicId)> InsertPricingPlanAsync(string name, string? description, byte billingPeriodId, decimal price, int? includesHours, string currencyCode, int? createdById);
        Task UpdatePricingPlanAsync(int id, string? name, string? description, byte? billingPeriodId, decimal? price, int? includesHours, string? currencyCode);
        Task DeletePricingPlanAsync(int id);

        // --- PlanFeature ---
        Task<IEnumerable<IDictionary<string, object?>>> GetPlanFeaturesByPlanIdAsync(int planId);
        Task<(int? Id, string? PublicId)> InsertPlanFeatureAsync(int planId, string featureName, string? featureValue, short sortOrder);
        Task UpdatePlanFeatureAsync(int id, string? featureName, string? featureValue, short? sortOrder);
        Task DeletePlanFeatureAsync(int id);

        // --- Location ---
        Task<IEnumerable<IDictionary<string, object?>>> GetAllLocationsAsync();
        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetLocationsAsync(int page, int limit, string? search);
        Task<(int? Id, string? PublicId)> InsertLocationAsync(int branchId, string name, string? address, int cityId, string? openingTime, string? closingTime, decimal? latitude, decimal? longitude, int? createdById);
        Task UpdateLocationAsync(int id, string? name, string? address, int? cityId, string? openingTime, string? closingTime, decimal? latitude, decimal? longitude);
        Task DeleteLocationAsync(int id);

        // --- Branch ---
        Task<IEnumerable<IDictionary<string, object?>>> GetAllBranchesAsync();
        Task<IEnumerable<IDictionary<string, object?>>> GetAllCompaniesAsync();
        Task<IEnumerable<IDictionary<string, object?>>> GetAllCitiesAsync();

        // --- Floor ---
        Task<IEnumerable<IDictionary<string, object?>>> GetFloorsAsync(int? locationId);
        Task<int?> InsertFloorAsync(int locationId, string name, short floorNumber, int? createdById);

        // --- Space ---
        Task<IEnumerable<IDictionary<string, object?>>> GetAllSpaceTypesAsync();
        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetSpaceTypesAsync(int page, int limit);
        Task<(int? Id, string? PublicId)> InsertSpaceTypeAsync(string name, string? description, byte? categoryId, short? capacity, bool hourlyAllowed, int? createdById);
        Task UpdateSpaceTypeAsync(int id, string? name, string? description, byte? categoryId, short? capacity, bool? hourlyAllowed, int? updatedById);
        Task DeleteSpaceTypeAsync(int id);

        // --- Space ---
        Task<IEnumerable<IDictionary<string, object?>>> GetSpaceConfigAsync();
        Task<IEnumerable<IDictionary<string, object?>>> GetSpaceConfigV2Async(int? companyId, int? branchId, int? locationId);
        Task<int> InsertSpaceConfigV2Async(SpaceConfigV2Request req, string? createdBy);
        Task UpdateSpaceConfigV2Async(int id, SpaceConfigV2Request req, string? updatedBy);
        Task DeleteSpaceConfigV2Async(int id);
        Task<decimal> GetSecurityDepositAsync(string category);
        Task UpdateSpaceConfigAsync(string category, string? updatedBy, int? totalSpaces, string? defaultCapacities, string? openingTime, string? closingTime, decimal? securityDeposit, decimal? price = null, byte? billingPeriodId = null);
        Task<IDictionary<string, object?>> GenerateSpaceInventoryAsync(int locationId, int spaceTypeId, string codePrefix, int minCode, int totalSpaces);
        Task<IEnumerable<IDictionary<string, object?>>> GetSpaceStatusForConfigAsync(int configId);
        Task<(List<string> Deleted, List<string> Blocked)> DeleteSpacesFromConfigAsync(int configId, string? spaceGuids);
        Task<IDictionary<string, object?>?> GetActivePricingForSpaceAsync(int spaceId);

        // --- Amenity ---
        Task<IEnumerable<IDictionary<string, object?>>> GetAllAmenitiesAsync();
        Task<int?> InsertAmenityAsync(string name, string? icon);

        // --- Gallery ---
        Task<IEnumerable<IDictionary<string, object?>>> GetAllGalleryImagesAsync(int? locationId);
        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetGalleryImagesAsync(int page, int limit, int? locationId);
        Task<(int? Id, string? PublicId)> InsertGalleryImageAsync(int? locationId, int? spaceId, string? title, string? description, string imageUrl, int sortOrder, int? createdById);
        Task UpdateGalleryImageAsync(int id, string? title, string? description, string? imageUrl, int? sortOrder);
        Task DeleteGalleryImageAsync(int id);

        // --- Contact ---
        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetContactsAsync(int page, int limit, string? search);
        Task<IEnumerable<IDictionary<string, object?>>> GetRecentContactsAsync(int top);
        Task<(int? Id, string? PublicId)> InsertContactAsync(string contactType, int? userId, string name, string email, string? phone, string? message);
        Task UpdateContactStatusAsync(int id, byte statusId, int? updatedById);
        Task DeleteContactAsync(int id);

        // --- Dashboard ---
        Task<IEnumerable<IEnumerable<IDictionary<string, object?>>>> GetDashboardSummaryAsync();

        // --- AccountCOA ---
        Task<IEnumerable<IDictionary<string, object?>>> GetAllAccountsCoaAsync();
        Task<IDictionary<string, object?>?> GetAccountCoaByIdAsync(int accountId);

        // --- AmountFields ---
        Task<IEnumerable<IDictionary<string, object?>>> GetAllAmountFieldsAsync();
        Task UpdateAmountFieldAccountAsync(int id, int? accountId);

        // --- Customer ---
        Task<IEnumerable<IDictionary<string, object?>>> GetAllCustomersAsync(int page, int limit, string? search);
        Task<IEnumerable<IDictionary<string, object?>>> SearchCustomersAsync(string query);
        Task<IDictionary<string, object?>?> GetCustomerByGuidAsync(string guid);
        Task<IDictionary<string, object?>?> GetCustomerByUserIdAsync(int userId);
        Task<IDictionary<string, object?>?> GetCustomerByEmailAsync(string email);
        Task<IDictionary<string, object?>> CreateCustomerAsync(string firstName, string? lastName, string email, string? phone, string? cnic, string? address, int? cityId, string? notes, string? createdBy, int? userId = null, string? company = null);
        Task<IDictionary<string, object?>?> UpdateCustomerAsync(string guid, string? firstName, string? lastName, string? email, string? phone, string? cnic, string? address, int? cityId, string? notes, bool? isActive, string? company = null);
        Task DeleteCustomerAsync(string guid);

        // --- Booking ---
        Task<(IDictionary<string, object?>? Header, IEnumerable<IDictionary<string, object?>> Lines)> GetBookingDetailsAsync(string bookingIdentifier, string? userEmail);
        Task<(IDictionary<string, object?>? ChallanHeader, IEnumerable<IDictionary<string, object?>> Lines)> GetChallanWithDetailsAsync(int bookingId);
        // --- AccessCard ---
        Task<(IEnumerable<IDictionary<string, object?>> Rows, int Total)> GetAccessCardsAsync(int page, int limit, string? search, int? bookingId, int? customerId, int? spaceId, int? status);
        Task<IDictionary<string, object?>?> GetAccessCardByIdAsync(string id);
        Task<IDictionary<string, object?>> CreateAccessCardAsync(int locationId, int customerId, int bookingId, int spaceId, string? cardNumber, DateTime startDate, DateTime endDate, int status, int? createdById);
        Task UpdateAccessCardAsync(string id, int? locationId, int? customerId, int? bookingId, int? spaceId, string? cardNumber, DateTime? startDate, DateTime? endDate, int? status, int? updatedById);
        Task DeleteAccessCardAsync(string id);
        Task GenerateAccessCardsForBookingDbAsync(int bookingId, int? createdById = null);
        Task ExecuteRawSqlAsync(string sql);

        // --- Attendants & Access Control ---
        Task<(int PersonId, Guid PersonGuid)> AddAttendantSpAsync(string name, string email, string phone, string idType, string idNumber, int customerId);
        Task UpdatePersonAsync(int personId, string name, string email, string phone);
        Task<IEnumerable<IDictionary<string, object?>>> GetCustomerAttendantsDbAsync(int customerId);
        Task<IEnumerable<IDictionary<string, object?>>> GetBookingAttendantsDbAsync(int bookingDetailId);
        Task<IDictionary<string, object?>> AssignAttendantToBookingSpAsync(int bookingDetailId, int personId, int customerId, DateTime assignedFrom);
        Task SoftRemoveAttendantFromBookingDbAsync(int bookingDetailId, int personId);
        Task<int> ToggleAccessStatusSpAsync(int bookingDetailId, int customerId, int? personId, bool isEnabled);
        Task<IEnumerable<IDictionary<string, object?>>> GetAccessStatusExportDbAsync();
        Task<IEnumerable<IDictionary<string, object?>>> GetCustomerActiveSpacesDbAsync(int customerId);
        Task<IDictionary<string, object?>?> GetBookingDetailSummaryDbAsync(int bookingDetailId);
        Task<IDictionary<string, object?>> CreateSurchargeInvoiceSpAsync(int bookingDetailId, int personId, int customerId, decimal surchargeAmount, int excessSeatCount);
    }
}

