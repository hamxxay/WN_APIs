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
