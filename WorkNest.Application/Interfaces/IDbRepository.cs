namespace WorkNest.Application.Interfaces
{
    public interface IDbRepository
    {
        // ── User ──────────────────────────────────────────────────────────────
        Task<(int? NumericId, string? Guid)> SyncUserAsync(string email, string firstName, string lastName, string? phone);
        Task<(int? NumericId, string? Guid)> GetUserIdByEmailAsync(string email);
        Task<IEnumerable<IDictionary<string, object?>>> GetAllUsersAsync();
        Task<IDictionary<string, object?>?> GetUserByIdAsync(string id);
        Task<IDictionary<string, object?>?> GetUserByGuidAsync(string guid);
        Task<IDictionary<string, object?>?> GetUserByEmailAsync(string email);
        Task UpdateUserAsync(string guid, string name, string? phone);
        Task SoftDeleteUserAsync(string guid);
        Task SetUserStatusAsync(string guid, int status);
        Task SetUserRoleAsync(string guid, int roleId);
        Task<IEnumerable<IDictionary<string, object?>>> GetBookingsByUserGuidAsync(string userGuid);
        Task<IEnumerable<IDictionary<string, object?>>> GetPaymentsByUserGuidAsync(string userGuid);

        // ── Space ─────────────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetAllSpacesAsync();
        Task<int?> InsertSpaceAsync(string name, string locationGuid, string spaceTypeGuid, string? code,
            string? description, int? floorId, double? pricePerDay, double? pricePerHour, double? pricePerMonth,
            string? imageUrl, string? amenities, int? rentAccountId = null, int? depositAccountId = null);
        Task UpdateSpaceAsync(string spaceGuid, string? name, string? locationGuid, string? spaceTypeGuid,
            string? code, string? description, int? floorId, double? pricePerDay,
            double? pricePerHour, double? pricePerMonth, string? imageUrl, string? amenities,
            int? rentAccountId = null, int? depositAccountId = null);
        Task SoftDeleteSpaceAsync(string guid);
        Task<int?> GetSpaceNumericIdByGuidAsync(string guid);
        Task<IDictionary<string, object?>?> GetSpaceSummaryAsync(string guid);
        Task<IEnumerable<IDictionary<string, object?>>> GetSpaceReservationsAsync(string guid);
        Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesAsync(string spaceType, string start, string end);
        Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesByTypeAsync(string spaceType, string? start, string? end);
        Task<IEnumerable<IDictionary<string, object?>>> GetAvailabilityCountsAsync();
        Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesV2Async(string spaceCategory, string start, string end, int? capacity);
        Task<IEnumerable<IDictionary<string, object?>>> GetAvailableSpacesForReassignmentAsync(string spaceType, string start, string end, int? excludeBookingId);

        // ── Booking ───────────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetAllBookingsAsync();
        Task<IEnumerable<IDictionary<string, object?>>> GetMyBookingsAsync(string userGuid);
        Task<IDictionary<string, object?>?> GetBookingByGuidAsync(string bookingGuid);
        Task<IDictionary<string, object?>> CreateBookingAsync(string userGuid, string spaceGuid, string start,
            string end, string notes, double amount, string? paymentMethod, string? paymentRef,
            string? customerCode = null);
        Task<IDictionary<string, object?>> CreateBookingWithAutoAssignmentAsync(string userEmail,
            string spaceType, string start, string end, string notes, double amount,
            string? paymentMethod, string? paymentRef);
        Task<IDictionary<string, object?>> CreateSmartBookingAsync(string userEmail, string spaceCategory,
            string start, string end, string notes, double amount, string? paymentMethod,
            string? paymentRef, int? capacity);
        Task CancelBookingAsync(string userGuid, string bookingGuid);
        Task UpdateBookingStatusAsync(string guid, int statusVal);
        Task UpdateBookingDatesAsync(string guid, string start, string end);
        Task<IDictionary<string, object?>> ReassignBookingAsync(string bookingGuid, string newSpaceGuid, string adminEmail);
        Task<IEnumerable<IDictionary<string, object?>>> GetBookingCalendarAsync(int spaceId, int year, int month);

        // ── Payment ───────────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetAllPaymentsAsync();
        Task<IEnumerable<IDictionary<string, object?>>> GetMyPaymentsAsync(string userGuid);
        Task<IDictionary<string, object?>> CreatePaymentAsync(string userGuid, string bookingGuid, double amount,
            string method, string transactionRef);
        Task UpdatePaymentStatusByRefAsync(string transactionRef, string status);
        Task UpdatePaymentStatusByGuidAsync(string guid, string status);
        Task SoftDeletePaymentAsync(string guid);

        // ── Location ──────────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetAllLocationsAsync();
        Task<int?> CreateLocationAsync(string name, string? address, int? cityId, string? openingTime,
            string? closingTime, bool isActive, int? branchId);
        Task UpdateLocationAsync(string guid, string name, string? address, int? cityId,
            string? openingTime, string? closingTime, bool isActive, int? branchId);
        Task SoftDeleteLocationAsync(string guid);

        // ── SpaceType ─────────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetAllSpaceTypesAsync();
        Task<int?> CreateSpaceTypeAsync(string name, int capacity, bool hourlyAllowed);
        Task UpdateSpaceTypeAsync(string guid, string name, int capacity, bool hourlyAllowed);
        Task SoftDeleteSpaceTypeAsync(string guid);

        // ── PricingPlan ───────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetAllPricingPlansAsync();
        Task<int?> CreatePricingPlanAsync(string name, double price, string? billingCycle, int includesHours, bool isActive);
        Task UpdatePricingPlanAsync(int id, string name, double price, string? billingCycle, int includesHours, bool isActive);
        Task SoftDeletePricingPlanAsync(int id);
        Task<IEnumerable<IDictionary<string, object?>>> GetMembershipsByPlanIdAsync(int planId);

        // ── Membership ────────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetAllMembershipsAsync();
        Task<int?> CreateMembershipAsync(string? userId, int planId, string startDate);
        Task UpdateMembershipStatusAsync(int id, string status);
        Task SoftDeleteMembershipAsync(int id);
        Task<IEnumerable<IDictionary<string, object?>>> GetPaymentsByMembershipIdAsync(int membershipId);

        // ── Contact ───────────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetAllContactsAsync();
        Task<int?> BookTourAsync(string name, string email, string message, string phone, int? userId);
        Task UpdateContactStatusAsync(string guid, string status);
        Task SoftDeleteContactAsync(string guid);

        // ── Gallery ───────────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetAllGalleryImagesAsync();
        Task<int?> CreateGalleryImageAsync(string? title, string imageUrl, int sortOrder, bool isActive);
        Task UpdateGalleryImageAsync(string id, string? title, string imageUrl, int sortOrder, bool isActive);
        Task SoftDeleteGalleryImageAsync(string id);

        // ── Floor ─────────────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetAllFloorsAsync(int? locationId);
        Task<int?> CreateFloorAsync(int locationId, string floorName);

        // ── Amenity ───────────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetAllAmenitiesAsync();
        Task<int?> CreateAmenityAsync(string name);

        // ── SpaceConfig ───────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetSpaceConfigAsync();
        Task<double> GetSecurityDepositAsync(string category);
        Task UpdateSpaceConfigAsync(string category, int totalSpaces, string? defaultCapacities,
            string? openingTime, string? closingTime, string? adminEmail, double? securityDeposit);
        Task<IDictionary<string, object?>> GenerateSpaceInventoryAsync(string spaceCategory,
            string spaceTypeId, string locationId, double pricePerHour, double pricePerDay, double pricePerMonth);

        // ── Customer ──────────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetAllCustomersAsync(int page, int limit, string search);
        Task<IEnumerable<IDictionary<string, object?>>> SearchCustomersAsync(string query);
        Task<IDictionary<string, object?>?> GetCustomerByGuidAsync(string guid);
        Task<IDictionary<string, object?>> CreateCustomerAsync(string firstName, string? lastName, string email,
            string? phoneNumber, string? cnicOrPassport, string? address, int? cityId, string? notes, string? createdBy);
        Task UpdateCustomerAsync(string guid, string? firstName, string? lastName, string? email,
            string? phoneNumber, string? cnicOrPassport, string? address, int? cityId, string? notes, bool? isActive);
        Task DeleteCustomerAsync(string guid);

        // ── Branch / Company / City ───────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetAllBranchesAsync();
        Task<IEnumerable<IDictionary<string, object?>>> GetAllCompaniesAsync();
        Task<IEnumerable<IDictionary<string, object?>>> GetAllCitiesAsync();

        // ── PlanFeature ───────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetPlanFeaturesByPlanIdAsync(int planId);
        Task<int?> CreatePlanFeatureAsync(int planId, string featureName);
        Task UpdatePlanFeatureAsync(int id, string featureName);
        Task SoftDeletePlanFeatureAsync(int id);

        // ── AccountCOA ────────────────────────────────────────────────────────
        Task<IEnumerable<IDictionary<string, object?>>> GetAllAccountsCoaAsync();
        Task<IDictionary<string, object?>?> GetAccountCoaByIdAsync(int accountId);
    }
}
