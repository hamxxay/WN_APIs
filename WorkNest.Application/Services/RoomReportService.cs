using WorkNest.Application.DTOs.Attendance;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Room Report: loads every archived door event of the chosen machines for a date range (no row cap; reuses the
    /// access-events query with an unbounded limit) and aggregates it with <see cref="RoomReportBuilder"/>.
    /// A customer maps to its booked rooms' machines through the read-only WN_Attendance_GetRooms.
    /// </summary>
    public class RoomReportService : IRoomReportService
    {
        public const int MaxDays = 92;

        /// <summary>Space categories without a room machine (same list as the attendance feature).</summary>
        private static readonly HashSet<string> NoRoomMachineCategories =
            new(StringComparer.OrdinalIgnoreCase) { "SharedSpace", "Shared", "Coworking", "MeetingRoom", "Conference" };

        private readonly IDbRepository _db;
        private readonly IAttendanceRepository _attendance;
        private readonly IBusinessClock _clock;

        public RoomReportService(IDbRepository db, IAttendanceRepository attendance, IBusinessClock clock)
        {
            _db = db;
            _attendance = attendance;
            _clock = clock;
        }

        public async Task<RoomReportOutcome> BuildAsync(IReadOnlyCollection<int> deviceIds, int? customerId, DateTime from, DateTime to, IReadOnlyCollection<int>? locationIds)
        {
            from = from.Date;
            to = to.Date;
            if (to < from) return RoomReportOutcome.Fail(400, "'to' must not be before 'from'.");
            if ((to - from).TotalDays + 1 > MaxDays) return RoomReportOutcome.Fail(400, $"The period can be at most {MaxDays} days.");

            var allDevices = (await _db.GetHikDevicesAsync(null, null)).ToList();
            var scoped = locationIds == null ? allDevices : (await _db.GetHikDevicesAsync(null, locationIds)).ToList();
            var scopedIds = scoped.Select(d => d.Id).ToHashSet();

            List<HikDeviceDto> chosen;
            if (customerId is int cid)
            {
                List<AttendanceRoomRow> rooms;
                try
                {
                    rooms = (await _attendance.GetRoomsAsync(cid, from, to))
                        .Where(r => !NoRoomMachineCategories.Contains((r.CategoryCode ?? "").Replace(" ", "")))
                        .ToList();
                }
                catch (AttendanceNotInstalledException)
                {
                    return RoomReportOutcome.Fail(503, AttendanceNotInstalledException.UserMessage);
                }

                var ids = new HashSet<int>();
                foreach (var room in rooms)
                {
                    IReadOnlyCollection<int>? roomLocations = room.LocationId is int l ? new[] { l } : null;
                    foreach (var d in HikEnrollmentService.RoomMachines(scoped, roomLocations, room.SpaceCode, room.SpaceName))
                        ids.Add(d.Id);
                }
                // Explicit machines on top of the customer's rooms are allowed too (still location-scoped).
                foreach (var id in deviceIds) ids.Add(id);
                if (ids.Count == 0)
                    return RoomReportOutcome.Fail(404, "No room machines found for that customer in this period.");
                chosen = ResolveDevices(ids, allDevices, scopedIds, out var denied);
                if (denied != null) return denied;
            }
            else
            {
                if (deviceIds.Count == 0) return RoomReportOutcome.Fail(400, "Pick at least one machine or a customer.");
                chosen = ResolveDevices(deviceIds, allDevices, scopedIds, out var denied);
                if (denied != null) return denied;
            }

            var rangeEnd = to.AddDays(1); // 'to' is inclusive; the query's upper bound is exclusive
            var batches = await Task.WhenAll(chosen.Select(d =>
                _db.GetHikAccessEventsDbAsync(from, rangeEnd, d.Id, null, null, int.MaxValue, null)));

            var rows = batches.SelectMany(b => b).Select(ToRow).Where(x => x.Time != default).ToList();
            // Events with no name on them (the machine was emptied, or never sent one): look the name up for ever.
            var unnamed = rows.Where(x => string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.EmployeeNo)).Select(x => x.EmployeeNo!.Trim()).Distinct().ToList();
            if (unnamed.Count > 0)
            {
                try { ApplyNameFallback(rows, await _attendance.GetEmployeeNamesAsync(unnamed)); }
                catch { /* names are a nicety: the report still works without them */ }
            }
            var machines = chosen.OrderBy(d => d.Code, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new RoomReportMachineDto { Id = d.Id, Name = d.Name, Code = d.Code, LocationName = d.LocationName }).ToList();
            return RoomReportOutcome.Ok(RoomReportBuilder.Build(machines, rows, from, to, _clock.Now));
        }

        /// <summary>Fills the name of events that carry none from the lookup (machine id -> name); events with a name are left alone.</summary>
        public static void ApplyNameFallback(IEnumerable<RoomReportEventRow> rows, IReadOnlyDictionary<string, string> names)
        {
            foreach (var row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.EmployeeNo)) continue;
                if (names.TryGetValue(row.EmployeeNo.Trim(), out var n) && !string.IsNullOrWhiteSpace(n)) row.Name = n;
            }
        }

        private static List<HikDeviceDto> ResolveDevices(IEnumerable<int> ids, List<HikDeviceDto> all, HashSet<int> scopedIds, out RoomReportOutcome? failure)
        {
            failure = null;
            var chosen = new List<HikDeviceDto>();
            foreach (var id in ids.Distinct())
            {
                var d = all.FirstOrDefault(x => x.Id == id);
                if (d == null) { failure = RoomReportOutcome.Fail(400, $"Unknown machine {id}."); return chosen; }
                if (!scopedIds.Contains(id)) { failure = RoomReportOutcome.Fail(403, "You can only report machines of your own locations."); return chosen; }
                chosen.Add(d);
            }
            return chosen;
        }

        private static RoomReportEventRow ToRow(IDictionary<string, object?> r)
        {
            string? Str(string k) => r.TryGetValue(k, out var v) && v != null ? Convert.ToString(v) : null;
            return new RoomReportEventRow
            {
                DeviceId = r.TryGetValue("device_id", out var d) && d != null ? Convert.ToInt32(d) : 0,
                DeviceName = Str("device_name"),
                EmployeeNo = Str("employee_no"),
                Name = Str("name"),
                AccessEvent = r.TryGetValue("access_event", out var a) && a != null ? Convert.ToInt32(a) : 0,
                Label = Str("label"),
                Time = r.TryGetValue("event_time", out var t) && t is DateTime dt ? dt : default
            };
        }
    }
}
