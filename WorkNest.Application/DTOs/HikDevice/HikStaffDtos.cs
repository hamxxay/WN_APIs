namespace WorkNest.Application.DTOs.HikDevice
{
    /// <summary>
    /// Building staff (janitors, office boys, …) — machine users with a job tag and no booking.
    /// Stored like the HIK dashboard does: the person lives on the machines, CNIC + tag in WN_HIK_Users.
    /// </summary>
    public class HikStaffDto
    {
        public string EmployeeNo { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Cnic { get; set; }
        public int? TagId { get; set; }
        public string? Tag { get; set; }
        /// <summary>False when blocked on any machine (Valid.enable = false).</summary>
        public bool Enabled { get; set; } = true;
        public string? ValidEnd { get; set; }
        public int Cards { get; set; }
        public int Fingerprints { get; set; }
        public int Faces { get; set; }
        public int PendingOps { get; set; }
        /// <summary>Has admin rights on a machine (localUIRight) — only shown to admin / super admin.</summary>
        public bool IsMachineAdmin { get; set; }
        public List<HikStaffMachineDto> Machines { get; set; } = new();
    }

    public class HikStaffMachineDto
    {
        public int DeviceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Grp { get; set; }
        public string? Code { get; set; }
        public bool Online { get; set; }
    }

    public class HikTagDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class HikTagCreateRequest
    {
        public string Name { get; set; } = string.Empty;
    }

    public class HikStaffCreateRequest
    {
        /// <summary>Set by the API from the caller's login (never from the request): their locations, or null for super admins.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public IReadOnlyCollection<int>? CallerLocationIds { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>13-digit CNIC (dashes allowed).</summary>
        public string Cnic { get; set; } = string.Empty;
        public int? TagId { get; set; }
        /// <summary>Extra (non-Entrance) machines; every Entrance machine is always included.</summary>
        public List<int> RoomDeviceIds { get; set; } = new();
        /// <summary>Last day of access (yyyy-MM-dd); empty = no end date.</summary>
        public string? ValidUntil { get; set; }
    }

    public class HikStaffMachinesRequest
    {
        /// <summary>Set by the API from the caller's login (never from the request): their locations, or null for super admins.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public IReadOnlyCollection<int>? CallerLocationIds { get; set; }
        public List<int> RoomDeviceIds { get; set; } = new();
        public string? ValidUntil { get; set; }
    }

    public class HikStaffEnableRequest
    {
        public bool IsEnabled { get; set; }
    }

    public class HikStaffEnrollRequest : HikEnrollRequest
    {
        /// <summary>Machine to capture on — one of the staff member's online machines.</summary>
        public int CaptureDeviceId { get; set; }
    }

    public class HikStaffResultDto
    {
        public bool Ok { get; set; }
        public string? MachineId { get; set; }
        public string? Error { get; set; }
        public List<HikEnrollDeviceResultDto> Devices { get; set; } = new();
    }
}
