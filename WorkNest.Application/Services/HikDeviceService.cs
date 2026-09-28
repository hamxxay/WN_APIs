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

        public async Task<HikRosterResponseDto> GetRostersAsync(string? location = null)
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

            var cnics = await _db.GetHikCnicMapAsync();

            return new HikRosterResponseDto
            {
                Ok = true,
                Rosters = rosters,
                Cnics = cnics
            };
        }

        public async Task<HikDeviceUsersResponseDto> GetDeviceUsersAsync(int deviceId)
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
    }
}
