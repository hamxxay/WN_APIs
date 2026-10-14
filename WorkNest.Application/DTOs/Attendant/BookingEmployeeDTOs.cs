using System;
using System.Collections.Generic;

namespace WorkNest.Application.DTOs.Attendant
{
    // Customer "Employees" on a booking (website My Bookings + app). A customer-added employee is a Pending request
    // (WN_BookingEmployeeRequests) until a sales executive checks their CNIC at the branch; then they are a booking
    // attendant like any other (WN_BookingAttendants), the people on the CMS Attendants & Access page.

    /// <summary>Body of POST api/booking/my/{bookingId}/employees.</summary>
    public class AddBookingEmployeeRequest
    {
        /// <summary>"new" (create a person) or "existing" (one of savedEmployees).</summary>
        public string? Mode { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        /// <summary>CNIC or Passport.</summary>
        public string? IdType { get; set; }
        public string? IdNumber { get; set; }
        /// <summary>Mode "existing": the person to add.</summary>
        public int? PersonId { get; set; }
    }

    /// <summary>Body of POST api/bookings/{bookingDetailId}/attendants/by-cnic.</summary>
    public class VerifyBookingEmployeeRequest
    {
        public string? IdNumber { get; set; }
    }

    /// <summary>GET api/booking/my/{bookingId}/employees (data).</summary>
    public class BookingEmployeesDto
    {
        public int BookingId { get; set; }
        public string? Space { get; set; }
        public string? Location { get; set; }
        public int Seats { get; set; }
        public int Used { get; set; }
        public bool CanManage { get; set; }
        /// <summary>Why the employees can't be changed (null when they can).</summary>
        public string? Reason { get; set; }
        public List<BookingEmployeeDto> Employees { get; set; } = new();
        public List<SavedEmployeeDto> SavedEmployees { get; set; } = new();
    }

    public class BookingEmployeeDto
    {
        public int PersonId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string IdType { get; set; } = string.Empty;
        public string IdNumber { get; set; } = string.Empty;
        /// <summary>yyyy-MM-dd (Pakistan date).</summary>
        public string? AddedOn { get; set; }
        /// <summary>"pending" (waiting for the CNIC check at the branch) or "active" (on the booking).</summary>
        public string Status { get; set; } = "active";
        /// <summary>"customer" or "staff".</summary>
        public string AddedBy { get; set; } = "staff";
        public BookingEmployeeAccessDto Access { get; set; } = new();
    }

    /// <summary>What WorkNest staff have enrolled on the machines so far (all false while pending).</summary>
    public class BookingEmployeeAccessDto
    {
        public bool Fingerprint { get; set; }
        public bool Face { get; set; }
        public bool Card { get; set; }
    }

    /// <summary>One of the customer's people who is not on this booking (POST mode "existing").</summary>
    public class SavedEmployeeDto
    {
        public int PersonId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IdType { get; set; } = string.Empty;
        public string IdNumber { get; set; } = string.Empty;
    }

    /// <summary>HTTP status + message + data of a booking-employee call (the controller wraps it in ApiResponse).</summary>
    public class BookingEmployeeOutcome
    {
        public int StatusCode { get; set; } = 200;
        public string Message { get; set; } = string.Empty;
        public object? Data { get; set; }
        public bool IsSuccessful => StatusCode is >= 200 and < 300;
    }

    // ---- rows of the WN_BookingEmployees_* procedures ----

    /// <summary>The customer's booking (WN_BookingEmployees_Get result 1; only returned to its owner).</summary>
    public class BookingEmployeeBookingRow
    {
        public int BookingId { get; set; }
        public int BookingStatusId { get; set; }
        public DateTime? StartOn { get; set; }
        public DateTime? EndOn { get; set; }
        public int? BookingDetailId { get; set; }
        public DateTime? DetailEnd { get; set; }
        public string? SpaceName { get; set; }
        public string? LocationName { get; set; }
        public int? CustomerId { get; set; }
    }

    /// <summary>A Pending or Verified request of the booking, with the person (WN_BookingEmployees_Get result 2).</summary>
    public class BookingEmployeeRequestRow
    {
        public int RequestId { get; set; }
        public int PersonId { get; set; }
        public string Status { get; set; } = string.Empty;
        /// <summary>UTC.</summary>
        public DateTime AddedAt { get; set; }
        public int? BookingDetailId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string IdType { get; set; } = string.Empty;
        public string IdNumber { get; set; } = string.Empty;
    }

    /// <summary>A person found by ID (WN_BookingEmployees_FindPerson).</summary>
    public class BookingEmployeePersonMatch
    {
        public int PersonId { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>Already one of this customer's people (WN_CustomerAttendants).</summary>
        public bool LinkedToCustomer { get; set; }
    }

    /// <summary>The Pending request for a CNIC on a booked space (WN_BookingEmployees_FindPendingByIdNumber).</summary>
    public class BookingEmployeePendingMatch
    {
        public int RequestId { get; set; }
        public int BookingId { get; set; }
        public int PersonId { get; set; }
        public int? CustomerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string IdType { get; set; } = string.Empty;
        public string IdNumber { get; set; } = string.Empty;
    }
}
