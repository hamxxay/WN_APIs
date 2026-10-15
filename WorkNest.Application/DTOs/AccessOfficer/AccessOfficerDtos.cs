using System.Text.Json.Serialization;
using WorkNest.Application.DTOs.Attendant;

namespace WorkNest.Application.DTOs.AccessOfficer
{
    // ---- Access Officer page (api/access-officer/*), see AccessOfficerService ----

    /// <summary>GET api/access-officer/lookup?cnic=...: the person and every booked space they are on (or waiting for).</summary>
    public class AccessOfficerLookupDto
    {
        /// <summary>The number searched: a CNIC as 12345-1234567-1, anything else as letters and digits, upper case.</summary>
        public string Cnic { get; set; } = string.Empty;
        /// <summary>The person (WN_Persons) of the first match.</summary>
        public AccessOfficerPersonDto? Person { get; set; }
        public List<AccessOfficerMatchDto> Matches { get; set; } = new();
    }

    public class AccessOfficerPersonDto
    {
        public int PersonId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        /// <summary>CNIC or Passport.</summary>
        public string IdType { get; set; } = string.Empty;
        /// <summary>A CNIC as 12345-1234567-1, a passport as stored.</summary>
        public string IdNumber { get; set; } = string.Empty;
    }

    /// <summary>One booked space the person is on ("assigned") or was added to by the customer ("pending").</summary>
    public class AccessOfficerMatchDto
    {
        public int BookingDetailId { get; set; }
        public int BookingId { get; set; }
        /// <summary>The person of this match (the one the enrol / remove / restore calls take).</summary>
        public int PersonId { get; set; }
        public string Room { get; set; } = string.Empty;
        public string? SpaceCode { get; set; }
        public string? Location { get; set; }
        public int? LocationId { get; set; }
        /// <summary>The booking owner: company, else name, else email.</summary>
        public string? Customer { get; set; }
        public string? BookingStatus { get; set; }
        /// <summary>yyyy-MM-dd.</summary>
        public string? StartOn { get; set; }
        /// <summary>yyyy-MM-dd.</summary>
        public string? EndOn { get; set; }
        /// <summary>pending = added by the customer, CNIC not checked yet; assigned = on the room.</summary>
        public string State { get; set; } = AccessOfficerStates.Pending;
        /// <summary>Assigned and the access status is on (false after Remove access, and for pending).</summary>
        public bool AccessEnabled { get; set; }
        /// <summary>Enrolled on the machines (best effort, from the machines' user lists).</summary>
        public BookingEmployeeAccessDto Credentials { get; set; } = new();
        /// <summary>The room's machine (enrolment captures there); null = no machine linked to the room.</summary>
        public AccessOfficerRoomMachineDto? RoomMachine { get; set; }
        public AccessOfficerSeatsDto? Seats { get; set; }
    }

    public static class AccessOfficerStates
    {
        public const string Pending = "pending";
        public const string Assigned = "assigned";
    }

    public class AccessOfficerRoomMachineDto
    {
        public string Name { get; set; } = string.Empty;
        public bool Online { get; set; }
    }

    public class AccessOfficerSeatsDto
    {
        /// <summary>People on the room + customer-added people still waiting for the CNIC check.</summary>
        public int Used { get; set; }
        /// <summary>The room's seats (same number as the Attendants page capacity check).</summary>
        public int Total { get; set; }
    }

    /// <summary>POST api/access-officer/remove-access and restore-access: the match after the change + the machines.</summary>
    public class AccessOfficerAccessChangeDto
    {
        public AccessOfficerMatchDto? Match { get; set; }
        /// <summary>Block / unblock on the room machine + every Entrance machine (queued for offline machines).</summary>
        public HikDevice.HikAccessSyncResultDto? Machines { get; set; }
    }

    // ---- request bodies ----

    public class AccessOfficerAddToRoomRequest
    {
        public int BookingDetailId { get; set; }
        public string? Cnic { get; set; }
    }

    public class AccessOfficerPersonRequest
    {
        public int BookingDetailId { get; set; }
        public int PersonId { get; set; }
    }

    public class AccessOfficerEnrollRequest : AccessOfficerPersonRequest
    {
        /// <summary>Fingerprint only: finger 1-10 (default 1).</summary>
        public int? FingerNo { get; set; }
    }

    // ---- service plumbing ----

    /// <summary>
    /// Who is asking: an Access Officer covers their own locations (<see cref="LocationIds"/>); a super admin every
    /// location (null). <see cref="UserId"/> = WN_Users.Id (VerifiedById of a CNIC check).
    /// </summary>
    public sealed record AccessOfficerScope(IReadOnlyCollection<int>? LocationIds, int? UserId)
    {
        public bool Covers(int? locationId) =>
            LocationIds == null || (locationId is int id && LocationIds.Contains(id));
    }

    /// <summary>Status code (200 / 400 / 403 / 404 / 409), message and data of one Access Officer call.</summary>
    public class AccessOfficerOutcome
    {
        public int StatusCode { get; set; } = 200;
        public string Message { get; set; } = "Success";
        public object? Data { get; set; }
        [JsonIgnore]
        public bool IsSuccessful => StatusCode is >= 200 and < 300;
    }

    /// <summary>One row of dbo.WN_AccessOfficer_Lookup (script AO_01).</summary>
    public class AccessOfficerMatchRow
    {
        public int BookingDetailId { get; set; }
        public int BookingId { get; set; }
        public int PersonId { get; set; }
        public string State { get; set; } = AccessOfficerStates.Pending;
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string IdType { get; set; } = string.Empty;
        public string IdNumber { get; set; } = string.Empty;
        public string Room { get; set; } = string.Empty;
        public string? SpaceCode { get; set; }
        public string? SpaceName { get; set; }
        public int? LocationId { get; set; }
        public string? Location { get; set; }
        public string? Customer { get; set; }
        public int? CustomerId { get; set; }
        public string? BookingStatus { get; set; }
        public DateTime? StartOn { get; set; }
        public DateTime? EndOn { get; set; }
        public bool AccessEnabled { get; set; }
        public string? MachineId { get; set; }
        public int ActiveCount { get; set; }
        public int PendingCount { get; set; }
    }
}
