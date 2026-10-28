using System.Globalization;
using WorkNest.Application.DTOs.Attendance;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Customer attendance. People = the customer's employees (WN_BookingAttendants of the customer, machine id from
    /// WN_HIK_PersonMap); machines = the room machines of the customer's booked rooms, matched exactly as the fingerprint
    /// enrolment does (<see cref="HikEnrollmentService.RoomMachines"/>: at the booking's location, not Entrance, Code
    /// matching the room), each counting only for the dates its booking covers. Shared space / meeting rooms have no
    /// room machine and are skipped. Days come from <see cref="AttendanceCalculator"/>.
    /// </summary>
    public class AttendanceService : IAttendanceService
    {
        private readonly IAttendanceRepository _repo;
        private readonly IDbRepository _db;
        private readonly IBusinessClock _clock;

        /// <summary>Space categories without a room machine (WN_SpaceCategories.Code / WN_BookingDetails.SpaceCategory, spaces removed).</summary>
        private static readonly HashSet<string> NoRoomMachineCategories =
            new(StringComparer.OrdinalIgnoreCase) { "SharedSpace", "Shared", "Coworking", "MeetingRoom", "Conference" };

        public AttendanceService(IAttendanceRepository repo, IDbRepository db, IBusinessClock clock)
        {
            _repo = repo;
            _db = db;
            _clock = clock;
        }

        public Task<AttendanceCustomerRow?> GetCustomerByUserAsync(int userId) => _repo.GetCustomerByUserAsync(userId);

        public Task<AttendanceCustomerRow?> GetCustomerAsync(int customerId) => _repo.GetCustomerAsync(customerId);

        public async Task<AttendanceOutcome> SetEnabledAsync(int customerId, bool enabled, string? by)
        {
            var c = await _repo.SetEnabledAsync(customerId, enabled, by);
            if (c == null) return AttendanceOutcome.Fail(404, "Customer not found.");
            return AttendanceOutcome.Ok(new AttendanceStatusDto { Enabled = c.Enabled, CustomerId = c.CustomerId, CustomerName = c.CustomerName },
                c.Enabled ? "Attendance turned on." : "Attendance turned off.");
        }

        public async Task<List<AttendanceCustomerDto>> GetCustomersAsync(IReadOnlyCollection<int>? locationIds) =>
            (await _repo.GetCustomersAsync(locationIds))
                .Select(c => new AttendanceCustomerDto { CustomerId = c.CustomerId, CustomerName = c.CustomerName, Enabled = c.Enabled, EmployeeCount = c.EmployeeCount })
                .ToList();

        // ---- Report ----

        public async Task<AttendanceOutcome> GetReportAsync(AttendanceCustomerRow customer, string? fromText, string? toText, int? personId)
        {
            var today = _clock.Today;
            DateTime from, to;
            if (string.IsNullOrWhiteSpace(fromText)) from = new DateTime(today.Year, today.Month, 1);
            else if (!TryDate(fromText, out from)) return AttendanceOutcome.Fail(400, "from must be a date (YYYY-MM-DD).");
            if (string.IsNullOrWhiteSpace(toText)) to = today < from ? from : today;
            else if (!TryDate(toText, out to)) return AttendanceOutcome.Fail(400, "to must be a date (YYYY-MM-DD).");
            if (to < from) return AttendanceOutcome.Fail(400, "to must be on or after from.");
            if ((to - from).Days + 1 > AttendanceCalculator.MaxRangeDays)
                return AttendanceOutcome.Fail(400, $"The date range can be at most {AttendanceCalculator.MaxRangeDays} days.");

            var report = new AttendanceReportDto
            {
                CustomerId = customer.CustomerId,
                CustomerName = customer.CustomerName,
                From = AttendanceCalculator.Date(from),
                To = AttendanceCalculator.Date(to)
            };

            var (people, assignments) = await _repo.GetPeopleAsync(customer.CustomerId);
            if (personId is > 0) people = people.Where(p => p.PersonId == personId).ToList();
            var periods = assignments.ToLookup(a => a.PersonId);
            // Only people who were an employee of the customer on some day of the range.
            people = people.Where(p => periods[p.PersonId].Any(a => a.AssignedFrom.Date <= to && (a.AssignedTo == null || a.AssignedTo.Value.Date >= from))).ToList();
            if (people.Count == 0) return AttendanceOutcome.Ok(report);

            var (shifts, ownShift) = await _repo.GetShiftsAsync(customer.CustomerId);
            var rules = shifts.ToDictionary(s => s.Id, ToRule);
            var defaultShift = shifts.FirstOrDefault(s => s.IsDefault);
            var holidays = (await _repo.GetHolidaysAsync(customer.CustomerId, from, to)).Select(h => h.HolidayDate.Date).ToHashSet();

            // Scans of the window days around the range (a shift day starts up to 4 h before / ends 20 h after its start).
            var scanFrom = from.AddDays(-1);
            var scanTo = to.AddDays(2);
            var deviceRooms = await DeviceRoomsAsync(customer.CustomerId, scanFrom, scanTo);
            var employeeNos = people.Where(p => !string.IsNullOrWhiteSpace(p.MachineId)).Select(p => p.MachineId!.Trim()).Distinct().ToList();
            var scans = deviceRooms.Count > 0 && employeeNos.Count > 0
                ? await _repo.GetScansAsync(deviceRooms.Keys.ToList(), employeeNos, scanFrom, scanTo)
                : new List<AttendanceScanRow>();
            var scansByEmployee = scans.ToLookup(s => s.EmployeeNo.Trim(), StringComparer.OrdinalIgnoreCase);
            var now = _clock.Now;

            foreach (var p in people)
            {
                // A scan counts on a machine of a room the customer had booked on that date (room change: old room
                // until the move, new room after).
                var personScans = string.IsNullOrWhiteSpace(p.MachineId)
                    ? new List<AttendanceScan>()
                    : scansByEmployee[p.MachineId.Trim()]
                        .Select(s => (s.EventTime, Room: RoomOn(deviceRooms, s.DeviceId, s.EventTime.Date)))
                        .Where(x => x.Room != null)
                        .Select(x => new AttendanceScan(x.EventTime, x.Room!.SpaceName))
                        .ToList();

                var shiftRow = ownShift.TryGetValue(p.PersonId, out var sid) && rules.ContainsKey(sid) ? shifts.First(s => s.Id == sid) : defaultShift;
                var rule = shiftRow == null ? null : rules[shiftRow.Id];
                var personPeriods = periods[p.PersonId].ToList();
                var days = AttendanceCalculator.ComputeDays(personScans, rule, holidays, from, to, now,
                    d => personPeriods.Any(a => a.AssignedFrom.Date <= d && (a.AssignedTo == null || a.AssignedTo.Value.Date >= d)));
                foreach (var d in days)
                {
                    d.PersonId = p.PersonId;
                    d.Name = p.Name;
                }
                report.Days.AddRange(days);
                report.Summary.Add(AttendanceCalculator.Summarize(p.PersonId, p.Name, days));
            }

            report.Days = report.Days.OrderByDescending(d => d.Date, StringComparer.Ordinal)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.PersonId).ToList();
            report.Summary = report.Summary.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.PersonId).ToList();
            return AttendanceOutcome.Ok(report);
        }

        /// <summary>Room machine id -> the customer's rooms on it (with their booked dates).</summary>
        private async Task<Dictionary<int, List<AttendanceRoomRow>>> DeviceRoomsAsync(int customerId, DateTime from, DateTime to)
        {
            var rooms = (await _repo.GetRoomsAsync(customerId, from, to))
                .Where(r => !NoRoomMachineCategories.Contains((r.CategoryCode ?? "").Replace(" ", "")))
                .ToList();
            var map = new Dictionary<int, List<AttendanceRoomRow>>();
            if (rooms.Count == 0) return map;

            var devices = (await _db.GetHikDevicesAsync()).ToList();
            foreach (var room in rooms)
            {
                IReadOnlyCollection<int>? locations = room.LocationId is int l ? new[] { l } : null;
                // The enrolment's own matching: the machines a fingerprint for this room is put on.
                foreach (var d in HikEnrollmentService.RoomMachines(devices, locations, room.SpaceCode, room.SpaceName))
                {
                    if (!map.TryGetValue(d.Id, out var list)) map[d.Id] = list = new List<AttendanceRoomRow>();
                    list.Add(room);
                }
            }
            return map;
        }

        private static AttendanceRoomRow? RoomOn(Dictionary<int, List<AttendanceRoomRow>> deviceRooms, int deviceId, DateTime date) =>
            deviceRooms.TryGetValue(deviceId, out var rooms)
                ? rooms.FirstOrDefault(r => (r.StartDate == null || r.StartDate.Value.Date <= date) && (r.EndDate == null || r.EndDate.Value.Date >= date))
                : null;

        // ---- Employees ----

        public async Task<List<AttendanceEmployeeDto>> GetEmployeesAsync(int customerId)
        {
            var (people, assignments) = await _repo.GetPeopleAsync(customerId);
            var (shifts, ownShift) = await _repo.GetShiftsAsync(customerId);
            return BuildEmployees(people, assignments, shifts, ownShift, _clock.Today);
        }

        private static List<AttendanceEmployeeDto> BuildEmployees(List<AttendancePersonRow> people, List<AttendanceAssignmentRow> assignments,
            List<AttendanceShiftRow> shifts, Dictionary<int, int> ownShift, DateTime today)
        {
            var byPerson = assignments.ToLookup(a => a.PersonId);
            var defaultShift = shifts.FirstOrDefault(s => s.IsDefault);
            var list = new List<AttendanceEmployeeDto>();
            foreach (var p in people)
            {
                var mine = byPerson[p.PersonId].ToList();
                var current = mine.Where(a => a.AssignedTo == null || a.AssignedTo.Value.Date >= today).ToList();
                var roomsFrom = current.Count > 0 ? current : mine.OrderByDescending(a => a.AssignedTo ?? DateTime.MaxValue).Take(1).ToList();
                var own = ownShift.TryGetValue(p.PersonId, out var sid) ? shifts.FirstOrDefault(s => s.Id == sid) : null;
                list.Add(new AttendanceEmployeeDto
                {
                    PersonId = p.PersonId,
                    Name = p.Name,
                    IdNumberMasked = MaskId(p.IdType, p.IdNumber),
                    Rooms = roomsFrom.Select(a => a.SpaceName).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).Distinct().ToList(),
                    ShiftId = own?.Id,
                    ShiftName = own?.Name ?? defaultShift?.Name,
                    UsesDefaultShift = own == null && defaultShift != null,
                    Active = current.Count > 0,
                    Enrolled = !string.IsNullOrWhiteSpace(p.MachineId)
                });
            }
            return list.OrderByDescending(e => e.Active).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>CNIC 3520212345671 -> "35202-*****-1"; other ids keep the first and last two characters.</summary>
        public static string MaskId(string? idType, string? idNumber)
        {
            var id = (idNumber ?? "").Trim();
            var digits = new string(id.Where(char.IsDigit).ToArray());
            if (!string.Equals(idType, "Passport", StringComparison.OrdinalIgnoreCase) && digits.Length == 13)
                return $"{digits[..5]}-*****-{digits[^1]}";
            if (id.Length <= 4) return new string('*', id.Length);
            return id[..2] + new string('*', id.Length - 4) + id[^2..];
        }

        // ---- Shifts ----

        public async Task<List<AttendanceShiftDto>> GetShiftsAsync(int customerId)
        {
            var (shifts, ownShift) = await _repo.GetShiftsAsync(customerId);
            var (people, assignments) = await _repo.GetPeopleAsync(customerId);
            var active = BuildEmployees(people, assignments, shifts, ownShift, _clock.Today).Where(e => e.Active).ToList();
            var defaultId = shifts.FirstOrDefault(s => s.IsDefault)?.Id;
            return shifts.Select(s => new AttendanceShiftDto
            {
                Id = s.Id,
                Name = s.Name,
                StartTime = AttendanceCalculator.HourMinute(s.StartTime),
                EndTime = AttendanceCalculator.HourMinute(s.EndTime),
                GraceMinutes = s.GraceMinutes,
                HalfDayMinutes = s.HalfDayMinutes,
                WorkDays = AttendanceCalculator.NormalizeWorkDays(s.WorkDays.Split(',')),
                IsDefault = s.IsDefault,
                CrossesMidnight = s.EndTime <= s.StartTime,
                // Active employees on this shift: their own, or the default one when they have none.
                EmployeeCount = active.Count(e => e.ShiftId == s.Id || (e.ShiftId == null && s.Id == defaultId))
            }).ToList();
        }

        public async Task<AttendanceOutcome> SaveShiftAsync(int customerId, int? id, AttendanceShiftInput? input, string? by)
        {
            if (input == null) return AttendanceOutcome.Fail(400, "Shift details are required.");
            var name = (input.Name ?? "").Trim();
            if (name.Length == 0) return AttendanceOutcome.Fail(400, "Shift name is required.");
            if (name.Length > 100) return AttendanceOutcome.Fail(400, "Shift name can be at most 100 characters.");
            var start = AttendanceCalculator.ParseTime(input.StartTime);
            var end = AttendanceCalculator.ParseTime(input.EndTime);
            if (start == null || end == null) return AttendanceOutcome.Fail(400, "Start and end time must be times (HH:mm).");
            if (input.GraceMinutes is < 0 or > 240) return AttendanceOutcome.Fail(400, "Grace minutes must be between 0 and 240.");
            if (input.HalfDayMinutes is not null and (< 1 or > 1440)) return AttendanceOutcome.Fail(400, "Half day minutes must be between 1 and 1440.");
            var days = AttendanceCalculator.NormalizeWorkDays(input.WorkDays);
            if (days.Count == 0) return AttendanceOutcome.Fail(400, "Choose at least one work day.");

            var savedId = await _repo.SaveShiftAsync(customerId, id, name, start.Value, end.Value, input.GraceMinutes,
                input.HalfDayMinutes, string.Join(",", days), input.IsDefault, by);
            if (savedId == null) return AttendanceOutcome.Fail(404, "Shift not found.");
            var saved = (await GetShiftsAsync(customerId)).FirstOrDefault(s => s.Id == savedId);
            return AttendanceOutcome.Ok(saved, id == null ? "Shift added." : "Shift saved.");
        }

        public async Task<AttendanceOutcome> DeleteShiftAsync(int customerId, int id, string? by) =>
            await _repo.DeleteShiftAsync(customerId, id, by) > 0
                ? AttendanceOutcome.Ok(null, "Shift deleted.")
                : AttendanceOutcome.Fail(404, "Shift not found.");

        public async Task<AttendanceOutcome> SetEmployeeShiftAsync(int customerId, int personId, int? shiftId, string? by)
        {
            var result = await _repo.SetEmployeeShiftAsync(customerId, personId, shiftId is > 0 ? shiftId : null, by);
            if (result == "person_not_found") return AttendanceOutcome.Fail(404, "Employee not found.");
            if (result == "shift_not_found") return AttendanceOutcome.Fail(404, "Shift not found.");
            if (result != "ok") return AttendanceOutcome.Fail(500, "The shift could not be saved.");
            var employee = (await GetEmployeesAsync(customerId)).FirstOrDefault(e => e.PersonId == personId);
            return AttendanceOutcome.Ok(employee, "Shift saved.");
        }

        // ---- Holidays ----

        public async Task<List<AttendanceHolidayDto>> GetHolidaysAsync(int customerId, int? year)
        {
            DateTime? from = year is >= 2000 and <= 2100 ? new DateTime(year.Value, 1, 1) : null;
            DateTime? to = from?.AddYears(1).AddDays(-1);
            return (await _repo.GetHolidaysAsync(customerId, from, to)).Select(ToDto).ToList();
        }

        public async Task<AttendanceOutcome> AddHolidayAsync(int customerId, AttendanceHolidayInput? input, string? by)
        {
            if (input == null || !TryDate(input.Date, out var date)) return AttendanceOutcome.Fail(400, "date must be a date (YYYY-MM-DD).");
            var name = (input.Name ?? "").Trim();
            if (name.Length == 0) return AttendanceOutcome.Fail(400, "Holiday name is required.");
            if (name.Length > 100) return AttendanceOutcome.Fail(400, "Holiday name can be at most 100 characters.");

            var (result, id) = await _repo.AddHolidayAsync(customerId, date, name, by);
            if (result == "exists") return AttendanceOutcome.Fail(409, "That date is already a holiday.");
            if (result != "ok" || id == null) return AttendanceOutcome.Fail(500, "The holiday could not be saved.");
            return AttendanceOutcome.Ok(new AttendanceHolidayDto { Id = id.Value, Date = AttendanceCalculator.Date(date), Name = name }, "Holiday added.");
        }

        public async Task<AttendanceOutcome> DeleteHolidayAsync(int customerId, int id) =>
            await _repo.DeleteHolidayAsync(customerId, id) > 0
                ? AttendanceOutcome.Ok(null, "Holiday deleted.")
                : AttendanceOutcome.Fail(404, "Holiday not found.");

        // ---- helpers ----

        private static AttendanceShiftRule ToRule(AttendanceShiftRow s) => new()
        {
            Name = s.Name,
            Start = s.StartTime,
            End = s.EndTime,
            GraceMinutes = s.GraceMinutes,
            HalfDayMinutes = s.HalfDayMinutes,
            WorkDays = AttendanceCalculator.ToDaysOfWeek(s.WorkDays.Split(','))
        };

        private static AttendanceHolidayDto ToDto(AttendanceHolidayRow h) =>
            new() { Id = h.Id, Date = AttendanceCalculator.Date(h.HolidayDate), Name = h.Name };

        private static bool TryDate(string? text, out DateTime date) =>
            DateTime.TryParseExact((text ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
