using WorkNest.Application.DTOs.Attendance;

namespace WorkNest.Application.Services
{
    /// <summary>A customer's shift as the calculator uses it. End &lt;= Start = the shift crosses midnight (night shift).</summary>
    public sealed class AttendanceShiftRule
    {
        public string Name { get; init; } = string.Empty;
        public TimeSpan Start { get; init; }
        public TimeSpan End { get; init; }
        public int GraceMinutes { get; init; }
        public int? HalfDayMinutes { get; init; }
        public IReadOnlySet<DayOfWeek> WorkDays { get; init; } = new HashSet<DayOfWeek>();

        public bool CrossesMidnight => End <= Start;

        /// <summary>Planned length: End - Start, + 24 h for a night shift.</summary>
        public TimeSpan Length => CrossesMidnight ? End - Start + TimeSpan.FromDays(1) : End - Start;
    }

    /// <summary>One successful scan of a person on a room machine of the customer (machine time, Pakistan).</summary>
    public readonly record struct AttendanceScan(DateTime Time, string? Room);

    /// <summary>
    /// The attendance days of ONE person, computed from their scans only (pure: no database, no clock - "now" is
    /// passed in). First scan of the day = check-in, last = check-out; one scan = check-in only (MissingOut once the
    /// day is over). Without a shift a day is a calendar date and only days with scans are listed (Present /
    /// MissingOut). With a shift the work date D is the date the shift STARTS and its scans are those in
    /// [D + start - 4 h, D + start + 20 h), so a night shift's morning scans belong to the evening it started.
    /// Days after "now" are never listed; today without scans is Pending until the shift has ended.
    /// </summary>
    public static class AttendanceCalculator
    {
        public const int MaxRangeDays = 62;

        private static readonly TimeSpan WindowBefore = TimeSpan.FromHours(4);
        private static readonly TimeSpan WindowAfter = TimeSpan.FromHours(20);

        /// <param name="scans">The person's scans (any order; ones outside the range are ignored).</param>
        /// <param name="shift">The person's shift (own or the customer's default); null = no shift.</param>
        /// <param name="holidays">The customer's holidays (dates).</param>
        /// <param name="from">First date of the report.</param>
        /// <param name="to">Last date of the report (included).</param>
        /// <param name="now">Current business time (Pakistan).</param>
        /// <param name="employedOn">False for dates the person was not an employee of the customer (not listed); null = always.</param>
        public static List<AttendanceDayDto> ComputeDays(IEnumerable<AttendanceScan> scans, AttendanceShiftRule? shift,
            IReadOnlySet<DateTime> holidays, DateTime from, DateTime to, DateTime now, Func<DateTime, bool>? employedOn = null)
        {
            var ordered = scans.OrderBy(s => s.Time).ToList();
            var today = now.Date;
            var last = to.Date < today ? to.Date : today;
            var days = new List<AttendanceDayDto>();

            for (var d = from.Date; d <= last; d = d.AddDays(1))
            {
                if (employedOn != null && !employedOn(d)) continue;
                var day = shift == null ? NoShiftDay(ordered, d, today) : ShiftDay(ordered, shift, holidays, d, now);
                if (day != null) days.Add(day);
            }
            return days;
        }

        private static AttendanceDayDto? NoShiftDay(List<AttendanceScan> scans, DateTime d, DateTime today)
        {
            var dayScans = scans.Where(s => s.Time.Date == d).ToList();
            if (dayScans.Count == 0) return null;

            var day = WithScans(d, dayScans);
            // Today with one scan: still at work, not MissingOut yet.
            day.Status = day.CheckOut == null && d < today ? AttendanceStatuses.MissingOut : AttendanceStatuses.Present;
            return day;
        }

        private static AttendanceDayDto ShiftDay(List<AttendanceScan> scans, AttendanceShiftRule shift, IReadOnlySet<DateTime> holidays,
            DateTime d, DateTime now)
        {
            var start = d + shift.Start;
            var end = start + shift.Length;
            var windowStart = start - WindowBefore;
            var windowEnd = start + WindowAfter;
            var isHoliday = holidays.Contains(d);
            var isWorkDay = shift.WorkDays.Contains(d.DayOfWeek);
            var over = now >= end;

            var dayScans = scans.Where(s => s.Time >= windowStart && s.Time < windowEnd).ToList();
            if (dayScans.Count == 0)
            {
                return new AttendanceDayDto
                {
                    Date = Date(d),
                    ShiftName = shift.Name,
                    Status = isHoliday ? AttendanceStatuses.Holiday
                        : !isWorkDay ? AttendanceStatuses.OffDay
                        : over ? AttendanceStatuses.Absent
                        : AttendanceStatuses.Pending
                };
            }

            var day = WithScans(d, dayScans);
            day.ShiftName = shift.Name;
            var checkIn = dayScans[0].Time;
            DateTime? checkOut = day.CheckOut == null ? null : dayScans[^1].Time;
            var worked = day.WorkedMinutes ?? 0;

            if (isHoliday || !isWorkDay)
            {
                // Worked on a holiday / off day: all worked time is overtime.
                day.OnHoliday = isHoliday;
                day.OnOffDay = !isHoliday;
                day.OvertimeMinutes = worked;
                day.Status = checkOut == null && over ? AttendanceStatuses.MissingOut : AttendanceStatuses.Present;
                return day;
            }

            var grace = TimeSpan.FromMinutes(Math.Max(0, shift.GraceMinutes));
            if (checkIn > start + grace) day.LateMinutes = Minutes(checkIn - start);
            // Early leave / half day only once the shift is over (a check-out so far may be a lunch break).
            if (over && checkOut is DateTime o && o < end - grace) day.EarlyLeaveMinutes = Minutes(end - o);
            if (checkOut != null) day.OvertimeMinutes = Math.Max(0, worked - Minutes(shift.Length));

            day.Status = checkOut == null && over ? AttendanceStatuses.MissingOut
                : checkOut != null && over && shift.HalfDayMinutes is > 0 && worked < shift.HalfDayMinutes ? AttendanceStatuses.HalfDay
                : day.LateMinutes > 0 ? AttendanceStatuses.Late
                : AttendanceStatuses.Present;
            return day;
        }

        /// <summary>Check-in / check-out / worked minutes / scans / room of a day that has scans (status set by the caller).</summary>
        private static AttendanceDayDto WithScans(DateTime d, List<AttendanceScan> dayScans)
        {
            var first = dayScans[0];
            var lastScan = dayScans[^1];
            var two = dayScans.Count >= 2;
            return new AttendanceDayDto
            {
                Date = Date(d),
                CheckIn = Time(first.Time),
                CheckOut = two ? Time(lastScan.Time) : null,
                WorkedMinutes = two ? Minutes(lastScan.Time - first.Time) : null,
                Scans = dayScans.Count,
                Room = first.Room
            };
        }

        /// <summary>Totals of one person's days. Present = days attended (Present + Late + HalfDay + MissingOut).</summary>
        public static AttendanceSummaryDto Summarize(int personId, string name, IEnumerable<AttendanceDayDto> days)
        {
            var s = new AttendanceSummaryDto { PersonId = personId, Name = name };
            foreach (var d in days)
            {
                switch (d.Status)
                {
                    case AttendanceStatuses.Present: s.Present++; break;
                    case AttendanceStatuses.Late: s.Present++; s.Late++; break;
                    case AttendanceStatuses.HalfDay: s.Present++; s.HalfDay++; break;
                    case AttendanceStatuses.MissingOut: s.Present++; s.MissingOut++; break;
                    case AttendanceStatuses.Absent: s.Absent++; break;
                    case AttendanceStatuses.Holiday: s.Holidays++; break;
                    case AttendanceStatuses.OffDay: s.OffDays++; break;
                }
                s.WorkedMinutes += d.WorkedMinutes ?? 0;
                s.OvertimeMinutes += d.OvertimeMinutes;
                s.LateMinutes += d.LateMinutes;
            }
            return s;
        }

        // ---- Shift fields ----

        public static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        /// <summary>"Mon,Tue" / ["monday","TUE"] -> the valid 3-letter names, Mon..Sun order, no repeats.</summary>
        public static List<string> NormalizeWorkDays(IEnumerable<string>? names) =>
            DayNames.Where(n => (names ?? Enumerable.Empty<string>())
                    .Any(x => (x ?? "").Trim().Length >= 3 && n.Equals((x ?? "").Trim()[..3], StringComparison.OrdinalIgnoreCase)))
                .ToList();

        public static HashSet<DayOfWeek> ToDaysOfWeek(IEnumerable<string> names) =>
            NormalizeWorkDays(names).Select(n => (DayOfWeek)((Array.IndexOf(DayNames, n) + 1) % 7)).ToHashSet();

        /// <summary>"9:00", "09:00", "09:00:00" -> time of day; null when not a time of day.</summary>
        public static TimeSpan? ParseTime(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!TimeSpan.TryParse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var t)) return null;
            if (t < TimeSpan.Zero || t >= TimeSpan.FromDays(1)) return null;
            return new TimeSpan(t.Hours, t.Minutes, 0);
        }

        public static string Date(DateTime d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        public static string Time(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        public static string HourMinute(TimeSpan t) => $"{t.Hours:00}:{t.Minutes:00}";

        private static int Minutes(TimeSpan t) => (int)Math.Floor(t.TotalMinutes);
    }
}
