using System.Text.Json.Nodes;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    public class HikDeviceService : IHikDeviceService
    {
        private readonly IDbRepository _db;
        private readonly IHikSyncRepository _hik;
        private readonly IHikSyncService _sync;

        public HikDeviceService(IDbRepository db, IHikSyncRepository hik, IHikSyncService sync)
        {
            _db = db;
            _hik = hik;
            _sync = sync;
        }

        public Task<bool> SetDeviceLocationAsync(int deviceId, int? locationId) =>
            _db.SetHikDeviceLocationDbAsync(deviceId, locationId);

        public async Task<IEnumerable<HikDeviceDto>> GetDevicesAsync(string? location = null, IReadOnlyCollection<int>? locationIds = null)
        {
            return await _db.GetHikDevicesAsync(location, locationIds);
        }

        public async Task<HikRosterResponseDto> GetRostersAsync(string? location = null, bool includeMachineAdmins = true, IReadOnlyCollection<int>? locationIds = null)
        {
            var snapshots = await _db.GetHikDeviceSnapshotsAsync(location);
            if (locationIds != null)
            {
                // Location-bound callers: only the machines of their locations (+ unassigned ones).
                var allowed = (await _db.GetHikDevicesAsync(null, locationIds)).Select(d => d.Id).ToHashSet();
                snapshots = snapshots.Where(x => allowed.Contains(x.DeviceId)).ToList();
            }
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
    
        // ---- machine management (admin / super admin) -------------------------------------------

        public Task<HikDeviceDetailDto?> GetDeviceAsync(int deviceId) => _hik.GetDeviceDetailAsync(deviceId);

        public Task<List<HikDeviceGroupDto>> GetDeviceGroupsAsync() => _hik.GetDeviceGroupsAsync();

        public async Task<HikDeviceSaveOutcome> AddDeviceAsync(HikDeviceSaveRequest? request, string? by)
        {
            var q = request ?? new HikDeviceSaveRequest();
            var error = Validate(q, adding: true);
            if (error != null) return Refused(400, error);

            q.Name = q.Name!.Trim();
            q.Host = q.Host!.Trim();
            q.Username = q.Username!.Trim();
            var port = q.Port is > 0 ? q.Port.Value : (q.UseHttps == true ? 443 : 80);
            var (result, id) = await _hik.AddDeviceAsync(q, port);
            switch (result)
            {
                case "Duplicate": return Refused(409, $"A machine already exists at {q.Host}:{port}.");
                case "BadLocation": return Refused(400, "Pick the machine's location.");
                case "Added" when id is > 0: break;
                default: return Refused(500, "The machine could not be saved.");
            }

            await Log(id, "add-machine", new { name = q.Name, host = q.Host, port, by });
            return await SavedAsync(id!.Value, $"{q.Name} added.", connectionChanged: true);
        }

        public async Task<HikDeviceSaveOutcome> UpdateDeviceAsync(int deviceId, HikDeviceSaveRequest? request, string? by)
        {
            var q = request ?? new HikDeviceSaveRequest();
            var error = Validate(q, adding: false);
            if (error != null) return Refused(400, error);

            var (result, otherId) = await _hik.UpdateDeviceAsync(deviceId, q);
            switch (result)
            {
                case "NotFound": return Refused(404, "Machine not found.");
                case "Duplicate": return Refused(409, $"Another machine (#{otherId}) already uses that IP / host and port.");
                case "BadLocation": return Refused(400, "Pick the machine's location.");
                case "Updated": break;
                default: return Refused(500, "The machine could not be saved.");
            }

            var connectionChanged = q.Host != null || q.Host2 != null || q.Port != null || q.UseHttps != null
                                    || q.Username != null || !string.IsNullOrEmpty(q.Password);
            await Log(deviceId, "edit-machine", new { name = q.Name?.Trim(), host = q.Host?.Trim(), port = q.Port, password = !string.IsNullOrEmpty(q.Password), by });
            return await SavedAsync(deviceId, "Machine saved.", connectionChanged);
        }

        public async Task<HikDeviceSaveOutcome> DeleteDeviceAsync(int deviceId, bool force, string? by)
        {
            var device = await _hik.GetDeviceDetailAsync(deviceId);
            if (device == null) return Refused(404, "Machine not found.");

            // People still enrolled on the machine (last cached roster, machine admins not counted).
            var enrolled = 0;
            try
            {
                var snap = await _hik.GetSnapshotAsync(deviceId);
                if (!string.IsNullOrWhiteSpace(snap?.Users) && WithoutMachineAdmins(JsonNode.Parse(snap.Users)) is JsonArray people)
                    enrolled = people.Count;
            }
            catch { /* no readable snapshot: nothing known enrolled */ }
            if (enrolled > 0 && !force)
                return Refused(409, $"{device.Name} still has {enrolled} {(enrolled == 1 ? "person" : "people")} enrolled. " +
                                    "Remove them from the machine first, or delete anyway.");

            await Log(deviceId, "delete-machine", new { name = device.Name, host = device.Host, port = device.Port, enrolled, force, by });
            var (result, _) = await _hik.DeleteDeviceAsync(deviceId);
            if (result == "NotFound") return Refused(404, "Machine not found.");
            if (result != "Deleted") return Refused(500, "The machine could not be deleted.");

            await InvalidateAsync(deviceId);
            return new HikDeviceSaveOutcome { Message = $"{device.Name} deleted." };
        }

        /// <summary>
        /// After a save: tests the machine so Online / Model / Serial are filled when it answers (an offline machine is
        /// still saved), forgets its cached roster when its connection changed, and returns it.
        /// </summary>
        private async Task<HikDeviceSaveOutcome> SavedAsync(int deviceId, string message, bool connectionChanged)
        {
            if (connectionChanged) await InvalidateAsync(deviceId);
            HikDeviceTestResult? test = null;
            try { test = await _sync.TestDeviceAsync(deviceId); }
            catch { /* the save stands; the online check retries */ }

            var note = test == null ? "" : test.Online ? " The machine is online." : $" The machine is offline: {test.Error}";
            return new HikDeviceSaveOutcome
            {
                Message = message + note,
                Device = await _hik.GetDeviceDetailAsync(deviceId),
                Test = test
            };
        }

        private async Task InvalidateAsync(int deviceId)
        {
            try { if (_sync is IHikRosterCache cache) await cache.InvalidateAsync(deviceId); }
            catch { /* next pass */ }
        }

        private async Task Log(int? deviceId, string action, object detail)
        {
            try { await _hik.LogAsync(null, deviceId, action, true, System.Text.Json.JsonSerializer.Serialize(detail)); }
            catch { /* the log never blocks the change */ }
        }

        private static HikDeviceSaveOutcome Refused(int status, string message) => new() { StatusCode = status, Message = message };

        /// <summary>Field rules (lengths = the procedure parameters). Adding: name, host, username, password, location required.</summary>
        private static string? Validate(HikDeviceSaveRequest q, bool adding)
        {
            static bool Blank(string? v) => string.IsNullOrWhiteSpace(v);
            static bool TooLong(string? v, int max) => v != null && v.Trim().Length > max;

            if (adding || q.Name != null)
            {
                if (Blank(q.Name)) return "Enter the machine name.";
                if (TooLong(q.Name, 100)) return "The name is too long (100 characters at most).";
            }
            if (adding || q.Host != null)
            {
                if (Blank(q.Host)) return "Enter the machine's IP address or host name.";
                if (TooLong(q.Host, 64) || !ValidHost(q.Host!)) return "Enter a valid IP address or host name (no http:// and no port).";
            }
            if (!Blank(q.Host2) && (TooLong(q.Host2, 128) || !ValidHost(q.Host2!)))
                return "Enter a valid failover IP address or host name, or leave it empty.";
            if (q.Port != null && (q.Port < 1 || q.Port > 65535)) return "The port must be between 1 and 65535.";
            if (adding || q.Username != null)
            {
                if (Blank(q.Username)) return "Enter the machine's username.";
                if (TooLong(q.Username, 64)) return "The username is too long (64 characters at most).";
            }
            if (adding && string.IsNullOrEmpty(q.Password)) return "Enter the machine's password.";
            if (q.Password != null && q.Password.Length > 128) return "The password is too long (128 characters at most).";
            if (TooLong(q.Location, 128)) return "The area is too long (128 characters at most).";
            if (TooLong(q.Grp, 64)) return "The group name is too long (64 characters at most).";
            if (TooLong(q.Code, 64)) return "The room code is too long (64 characters at most).";
            if (adding && q.LocationId is not > 0) return "Pick the machine's location.";
            if (q.LocationId != null && q.LocationId <= 0) return "Pick the machine's location.";
            return null;
        }

        private static bool ValidHost(string host) =>
            Uri.CheckHostName(host.Trim()) is UriHostNameType.IPv4 or UriHostNameType.Dns or UriHostNameType.IPv6;

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
