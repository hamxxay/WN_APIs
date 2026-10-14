using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Attendant;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Customer-added employees waiting for the CNIC check (dbo.WN_BookingEmployeeRequests), through the
    /// dbo.WN_BookingEmployees_* procedures of the EMP_01 database update. Every call throws
    /// <see cref="BookingEmployeesNotInstalledException"/> while that update has not been run.
    /// </summary>
    public interface IBookingEmployeeRepository
    {
        /// <summary>The booking (null unless it belongs to the user) and its Pending / Verified requests.</summary>
        Task<(BookingEmployeeBookingRow? Booking, List<BookingEmployeeRequestRow> Requests)> GetAsync(int bookingId, int userId);

        /// <summary>A person by ID (same key as WN_AddAttendant), or null when WN_AddAttendant would create a new one.</summary>
        Task<BookingEmployeePersonMatch?> FindPersonAsync(string idType, string idNumber, int customerId);

        /// <summary>New Pending request; Result is "ok", "duplicate" (already pending / on the room) or "full".</summary>
        Task<(string Result, int? RequestId, int Used)> AddRequestAsync(int bookingId, int bookingDetailId, int? customerId, int personId, int? addedByUserId, int seats);

        /// <summary>The person's Pending / Verified requests on the booking become Removed; returns how many.</summary>
        Task<int> RemoveRequestsAsync(int bookingId, int personId);

        /// <summary>The Pending request for a CNIC (13 digits) on the booking behind a booked space.</summary>
        Task<BookingEmployeePendingMatch?> FindPendingByIdNumberAsync(int bookingDetailId, string idNumberDigits);

        /// <summary>
        /// Pending -> Verified: one request (requestId), or every Pending request of a person on the booking behind
        /// a booked space (requestId null). Returns how many were verified.
        /// </summary>
        Task<int> VerifyAsync(int? requestId, int? bookingDetailId, int? personId, int? verifiedById);

        /// <summary>Verified -> Pending again (adding the person to the space failed).</summary>
        Task ReopenAsync(int requestId);
    }

    /// <summary>The EMP_01 database update (table WN_BookingEmployeeRequests + procedures) has not been run yet.</summary>
    public class BookingEmployeesNotInstalledException : Exception
    {
        public const string UserMessage =
            "Employees are not available yet: the database needs the update EMP_01 (EMP_01_customer_employees). Please try again later.";

        public BookingEmployeesNotInstalledException(Exception inner) : base(UserMessage, inner) { }
    }
}
