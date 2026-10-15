using System.Threading.Tasks;
using WorkNest.Application.DTOs.Attendant;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Customer "Employees" on a booking. The owner (role general) adds people as Pending requests, which a sales
    /// executive verifies by CNIC at the branch; verified people are ordinary booking attendants (Attendants &amp;
    /// Access). Throws <see cref="BookingEmployeesNotInstalledException"/> until the EMP_01 database update is run.
    /// </summary>
    public interface IBookingEmployeeService
    {
        /// <summary>The signed-in user's WN_Users.Id (by email; older tokens carried the numeric id).</summary>
        Task<int?> ResolveUserIdAsync(string? email, string? idClaim);

        Task<BookingEmployeeOutcome> GetAsync(int bookingId, int userId);
        Task<BookingEmployeeOutcome> AddAsync(int bookingId, int userId, AddBookingEmployeeRequest? body);
        Task<BookingEmployeeOutcome> RemoveAsync(int bookingId, int userId, int personId);

        /// <summary>
        /// Sales: verify a customer-added employee by CNIC and add them to the booked space. <paramref name="acceptOtherIds"/>
        /// (Access Officer): a passport / other ID number is accepted too (letters and digits), not only a 13-digit CNIC.
        /// </summary>
        Task<BookingEmployeeOutcome> VerifyByCnicAsync(int bookingDetailId, string? idNumber, int? staffUserId, bool allowOverCapacity,
            bool acceptOtherIds = false);

        /// <summary>
        /// Staff added a person to a booked space directly: a Pending request of that person on the booking is
        /// marked Verified. Best effort (nothing happens before the EMP_01 update is run).
        /// </summary>
        Task MarkVerifiedIfRequestedAsync(int bookingDetailId, int personId, int? staffUserId);

        /// <summary>People a customer added who are still waiting for the CNIC check and are not active on any booking:
        /// hidden from a sales executive's "someone already on this company" list (empty before EMP_01).</summary>
        Task<HashSet<int>> GetPendingOnlyPersonIdsAsync(int customerId);

        /// <summary>True when the customer added this person to this booking and the CNIC check has not been done yet.</summary>
        Task<bool> HasPendingRequestAsync(int bookingDetailId, int personId);
    }
}
