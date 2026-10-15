using WorkNest.Application.DTOs.AccessOfficer;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// dbo.WN_AccessOfficer_Lookup (script AO_01_access_officer): the people on a confirmed booking that has not ended,
    /// by CNIC / passport number (letters and digits, upper case) and / or person, optionally one booked space, limited
    /// to locations (CSV, null = all). Throws <see cref="AccessOfficerDatabaseUpdateMissingException"/> before AO_01.
    /// </summary>
    public interface IAccessOfficerRepository
    {
        Task<List<AccessOfficerMatchRow>> LookupAsync(string? idKey, int? personId, int? bookingDetailId, string? locationIds);
    }

    /// <summary>
    /// Access Officer page: find a person by CNIC on the confirmed bookings of the caller's locations, add a
    /// customer-added person to the room (the sales CNIC check), enrol face / fingerprint on the room machine (+ every
    /// Entrance machine), remove or restore their door access (they stay on the booking). Reuses the Attendants /
    /// customer-employees / Hikvision services, so every step behaves as on the Attendants &amp; Access page.
    /// </summary>
    public interface IAccessOfficerService
    {
        Task<AccessOfficerOutcome> LookupAsync(string? cnic, AccessOfficerScope scope);
        Task<AccessOfficerOutcome> AddToRoomAsync(AccessOfficerAddToRoomRequest? request, AccessOfficerScope scope);
        /// <summary><paramref name="kind"/> = face | fingerprint.</summary>
        Task<AccessOfficerOutcome> EnrollAsync(string kind, AccessOfficerEnrollRequest? request, AccessOfficerScope scope);
        Task<AccessOfficerOutcome> SetAccessAsync(AccessOfficerPersonRequest? request, bool enable, AccessOfficerScope scope);
    }

    /// <summary>dbo.WN_AccessOfficer_Lookup (script AO_01) is not on this database yet (answered 503).</summary>
    public sealed class AccessOfficerDatabaseUpdateMissingException : Exception
    {
        public const string UserMessage = "The Access page needs the database update AO_01.";

        public AccessOfficerDatabaseUpdateMissingException(Exception inner) : base(UserMessage, inner)
        {
            Detail = inner.Message;
        }

        /// <summary>The database's own error text (for the server log).</summary>
        public string Detail { get; }
    }
}
