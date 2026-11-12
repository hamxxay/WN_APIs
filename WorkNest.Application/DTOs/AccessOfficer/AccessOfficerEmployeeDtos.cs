namespace WorkNest.Application.DTOs.AccessOfficer
{
    /// <summary>GET api/access-officer/employees: filter values.</summary>
    public static class AccessOfficerEmployeeFilters
    {
        /// <summary>On the room and no fingerprint and no face (default).</summary>
        public const string Needs = "needs";
        /// <summary>On the room with a fingerprint or a face.</summary>
        public const string Enrolled = "enrolled";
        public const string All = "all";
    }

    /// <summary>One person on a booked room (or waiting for the CNIC check) with what is enrolled on the room machine.</summary>
    public class AccessOfficerEmployeeDto
    {
        public int PersonId { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>CNIC as 12345-1234567-1, a passport as stored.</summary>
        public string? IdNumber { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        /// <summary>The booking owner: company, else name, else email.</summary>
        public string? CustomerName { get; set; }
        public int BookingId { get; set; }
        public int BookingDetailId { get; set; }
        /// <summary>Space name.</summary>
        public string Room { get; set; } = string.Empty;
        public string? Location { get; set; }
        public int? LocationId { get; set; }
        /// <summary>When the customer added the person (yyyy-MM-ddTHH:mm:ssZ); null when staff put them on the room.</summary>
        public string? AddedOn { get; set; }
        /// <summary>"Customer &lt;email&gt;" / "Staff &lt;email&gt;"; null when unknown.</summary>
        public string? AddedBy { get; set; }
        public bool Fingerprint { get; set; }
        public bool Face { get; set; }
        public bool Card { get; set; }
        public bool AccessEnabled { get; set; }
        /// <summary>true = assigned to the room; false = added by the customer, CNIC not checked yet.</summary>
        public bool OnRoom { get; set; }
        /// <summary>The room's machine (enrolment captures there); null = none linked.</summary>
        public AccessOfficerRoomMachineDto? RoomMachine { get; set; }
    }

    /// <summary>One row of the employees query (see AccessOfficerRepository.EmployeesAsync).</summary>
    public class AccessOfficerEmployeeRow
    {
        public int PersonId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? IdType { get; set; }
        public string? IdNumber { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Customer { get; set; }
        public int BookingId { get; set; }
        public int BookingDetailId { get; set; }
        public string Room { get; set; } = string.Empty;
        public string? SpaceCode { get; set; }
        public string? SpaceName { get; set; }
        public int? LocationId { get; set; }
        public string? Location { get; set; }
        public DateTime? StartOn { get; set; }
        public DateTime? AddedAtUtc { get; set; }
        public string? AddedByEmail { get; set; }
        /// <summary>A customer request (WN_BookingEmployeeRequests) added this person.</summary>
        public bool AddedByCustomer { get; set; }
        public bool OnRoom { get; set; }
        public bool AccessEnabled { get; set; }
        public string? MachineId { get; set; }
    }
}
