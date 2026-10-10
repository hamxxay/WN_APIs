namespace WorkNest.Application.Interfaces
{
    /// <summary>Kinds of record whose location decides who may open them (see RecordScopeAttribute).</summary>
    public enum RecordKind { Booking, Payment, Agreement, Customer, User }

    /// <summary>Which WN_Locations a single record belongs to, for location-bound admins / sales executives.</summary>
    public interface IRecordScopeRepository
    {
        /// <summary>
        /// Locations of the record identified by an int id, a GUID or (users only) an email.
        /// Null = no such record; an empty list = the record has no location (e.g. a customer with no booking yet).
        /// </summary>
        Task<List<int>?> GetLocationIdsAsync(RecordKind kind, string key);
        /// <summary>Payment id for a payment public id (WN_Payments.IdGUID); null when not found.</summary>
        Task<int?> GetPaymentIdByPublicIdAsync(Guid publicId);
        /// <summary>Ids of the payments whose booking (or invoice's booking) is in one of these locations.</summary>
        Task<HashSet<int>> GetPaymentIdsInLocationsAsync(IEnumerable<int> locationIds);
    }
}
