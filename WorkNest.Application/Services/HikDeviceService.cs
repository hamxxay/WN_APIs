using System.Text.Json.Nodes;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    public class HikDeviceService : IHikDeviceService
    {
        private readonly IDbRepository _db;

        public HikDeviceService(IDbRepository db)
        {
            _db = db;
        }

        public async Task<IEnumerable<HikDeviceDto>> GetDevicesAsync(string? location = null)
        {
            return await _db.GetHikDevicesAsync(location);
        }

        public async Task<HikRosterResponseDto> GetRostersAsync(string? location = null, bool includeMachineAdmins = true)
        {
            var snapshots = await _db.GetHikDeviceSnapshotsAsync(location);
            var rosters = new List<HikDeviceRosterItemDto>();

            foreach (var (deviceId, rosterJson) in snapshots)
            {
                if (string.IsNullOrWhiteSpace(rosterJson))
                {
                    rosters.Add(new HikDeviceRosterItemDto
                    {
                        DeviceId = deviceId,
                        Ok = false,
                        Error = "no snapshot yet"
                    });
                    continue;
                }

                try
                {
                    var usersNode = JsonNode.Parse(rosterJson);
                    if (!includeMachineAdmins) usersNode = WithoutMachineAdmins(usersNode);
                    rosters.Add(new HikDeviceRosterItemDto
                    {
                        DeviceId = deviceId,
                        Ok = true,
                        Users = usersNode
                    });
                }
                catch
                {
                    rosters.Add(new HikDeviceRosterItemDto
                    {
                        DeviceId = deviceId,
                        Ok = false,
                        Error = "corrupt snapshot"
                    });
                }
            }

            var cnicsTask = _db.GetHikCnicMapAsync();
            var bookedTask = _db.GetHikBookedRoomsDbAsync();
            var tagsTask = _db.GetHikStaffTagMapDbAsync();
            await Task.WhenAll(cnicsTask, bookedTask, tagsTask);

            var bookings = bookedTask.Result
                .GroupBy(r => Convert.ToString(r["employee_no"]) ?? "")
                .ToDictionary(g => g.Key, g => g.Select(r => new HikBookedRoomDto
                {
                    Space = Convert.ToString(r["space"]) ?? "",
                    SpaceCode = Convert.ToString(r["space_code"]),
                    Customer = Convert.ToString(r["customer"]),
                    BookingEnd = r["booking_end"] is DateTime e ? e.ToString("yyyy-MM-ddTHH:mm:ss") : null
                }).ToList());

            return new HikRosterResponseDto
            {
                Ok = true,
                Rosters = rosters,
                Cnics = cnicsTask.Result,
                Bookings = bookings,
                Tags = tagsTask.Result
            };
        }

        public async Task<HikDeviceUsersResponseDto> GetDeviceUsersAsync(int deviceId, bool includeMachineAdmins = true)
        {
            var rosterJson = await _db.GetHikDeviceSnapshotByIdAsync(deviceId);
            if (string.IsNullOrWhiteSpace(rosterJson))
            {
                return new HikDeviceUsersResponseDto
                {
                    Ok = false,
                    Error = "no snapshot for this machine yet"
                };
            }

            try
            {
                var usersNode = JsonNode.Parse(rosterJson);
                if (!includeMachineAdmins) usersNode = WithoutMachineAdmins(usersNode);
                var total = usersNode is JsonArray arr ? arr.Count : 0;

                return new HikDeviceUsersResponseDto
                {
                    Ok = true,
                    Total = total,
                    Users = usersNode
                };
            }
            catch
            {
                return new HikDeviceUsersResponseDto
                {
                    Ok = false,
                    Error = "corrupt snapshot"
                };
            }
        }

        public async Task<HikNextEmployeeNoResponseDto> GetNextEmployeeNoAsync()
        {
            var next = await _db.GetNextHikEmployeeNoAsync();
            return new HikNextEmployeeNoResponseDto
            {
                Ok = true,
                Next = next
            };
        }
    
        /// <summary>Drops machine-admin users (UserInfo.localUIRight / userType "admin") from a roster array.</summary>
        public static JsonNode? WithoutMachineAdmins(JsonNode? users)
        {
            if (users is not JsonArray arr) return users;
            var kept = new JsonArray();
            foreach (var u in arr)
            {
                if (u == null || IsMachineAdmin(u)) continue;
                kept.Add(u.DeepClone());
            }
            return kept;
        }

        public static bool IsMachineAdmin(JsonNode u)
        {
            var right = u["localUIRight"]?.ToString();
            return string.Equals(right, "true", StringComparison.OrdinalIgnoreCase) || right == "1"
                   || string.Equals(u["userType"]?.ToString(), "admin", StringComparison.OrdinalIgnoreCase);
        }
    }
}
