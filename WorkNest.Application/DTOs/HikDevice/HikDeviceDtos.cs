using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WorkNest.Application.DTOs.HikDevice
{
    public class HikDeviceDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("grp")]
        public string? Grp { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        /// <summary>WorkNest location the machine belongs to (WN_Locations.Id); null = not assigned.</summary>
        [JsonPropertyName("locationId")]
        public int? LocationId { get; set; }

        [JsonPropertyName("locationName")]
        public string? LocationName { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("online")]
        public int Online { get; set; }

        [JsonPropertyName("last_seen")]
        public string? LastSeen { get; set; }
    }

    /// <summary>One machine for the CMS edit form (admin / super admin). Never carries the password.</summary>
    public class HikDeviceDetailDto : HikDeviceDto
    {
        [JsonPropertyName("host")]
        public string Host { get; set; } = string.Empty;

        /// <summary>Failover IP / host, tried when Host does not answer.</summary>
        [JsonPropertyName("host2")]
        public string? Host2 { get; set; }

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("useHttps")]
        public bool UseHttps { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("serial")]
        public string? Serial { get; set; }
    }

    /// <summary>A machine group (WN_HIK_Groups). A group starting with "Entrance" marks entrance doors.</summary>
    public class HikDeviceGroupDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("isEntrance")]
        public bool IsEntrance { get; set; }

        [JsonPropertyName("machines")]
        public int Machines { get; set; }
    }

    /// <summary>
    /// Body of POST / PUT api/hik/devices. Add: Name, Host, Username, Password and LocationId are required. Edit: a
    /// field left null is kept, an empty Password keeps the saved one, "" clears Host2 / Location / Grp / Code.
    /// </summary>
    public class HikDeviceSaveRequest
    {
        public string? Name { get; set; }
        public string? Host { get; set; }
        public string? Host2 { get; set; }
        /// <summary>Default 80 (443 with HTTPS).</summary>
        public int? Port { get; set; }
        public bool? UseHttps { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        /// <summary>Area / label, e.g. "Entrance" or "Room 313" (WN_HIK_Devices.Location).</summary>
        public string? Location { get; set; }
        /// <summary>Group name; a new name creates the group.</summary>
        public string? Grp { get; set; }
        /// <summary>Room number the machine opens (e.g. "313"), used to match booked rooms. Empty for entrance / common doors.</summary>
        public string? Code { get; set; }
        /// <summary>WorkNest location (WN_Locations.Id).</summary>
        public int? LocationId { get; set; }
    }

    /// <summary>Result of an add / edit / delete: HTTP status, message and (add / edit) the saved machine.</summary>
    public class HikDeviceSaveOutcome
    {
        public int StatusCode { get; set; } = 200;
        public bool IsSuccessful => StatusCode is >= 200 and < 300;
        public string Message { get; set; } = string.Empty;
        public HikDeviceDetailDto? Device { get; set; }
        /// <summary>The connection test run after the save (null when it could not run).</summary>
        public HikDeviceTestResult? Test { get; set; }
    }

    public class HikDeviceRosterItemDto
    {
        [JsonPropertyName("device_id")]
        public int DeviceId { get; set; }

        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Error { get; set; }

        [JsonPropertyName("users")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonNode? Users { get; set; }
    }

    public class HikRosterResponseDto
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; } = true;

        [JsonPropertyName("rosters")]
        public List<HikDeviceRosterItemDto> Rosters { get; set; } = new();

        [JsonPropertyName("cnics")]
        public Dictionary<string, string> Cnics { get; set; } = new();

        /// <summary>employeeNo → active booked rooms (attendants enrolled from Attendants &amp; Access).</summary>
        [JsonPropertyName("bookings")]
        public Dictionary<string, List<HikBookedRoomDto>> Bookings { get; set; } = new();

        /// <summary>employeeNo → job tag (staff: Janitor, Office Boy, …).</summary>
        [JsonPropertyName("tags")]
        public Dictionary<string, string> Tags { get; set; } = new();
    }

    public class HikBookedRoomDto
    {
        [JsonPropertyName("space")]
        public string Space { get; set; } = string.Empty;

        [JsonPropertyName("space_code")]
        public string? SpaceCode { get; set; }

        [JsonPropertyName("customer")]
        public string? Customer { get; set; }

        [JsonPropertyName("booking_end")]
        public string? BookingEnd { get; set; }
    }

    public class HikDeviceUsersResponseDto
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Error { get; set; }

        [JsonPropertyName("users")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonNode? Users { get; set; }
    }

    public class HikNextEmployeeNoResponseDto
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; } = true;

        [JsonPropertyName("next")]
        public int Next { get; set; }
    }
}
