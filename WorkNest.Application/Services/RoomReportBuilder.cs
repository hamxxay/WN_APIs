using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Pure aggregation of door events (WN_HIK_Events, machine local time) into the staff Room Report.
    /// Hikvision minor codes: 1 card, 38 fingerprint, 75/104 face = entries; 8/9/39/76/112 = denied;
    /// 21/22 door unlocked/locked; 23/24 exit button pressed/released (no person).
    /// </summary>
    public static class RoomReportBuilder
    {
        public const string TimeFormat = "yyyy-MM-ddTHH:mm:ss";
        public const string DateFormat = "yyyy-MM-dd";

        private static readonly Dictionary<int, string> Methods = new()
        {
            [1] = "Card", [38] = "Fingerprint", [75] = "Face", [104] = "Face"
        };

        private static readonly Dictionary<int, string> DeniedLabels = new()
        {
            [39] = "Fingerprint not matched", [76] = "Face not recognised", [8] = "Card verify failed",
            [9] = "Unregistered card", [112] = "Entry denied (expired)"
        };

        private static readonly Dictionary<int, string> OtherLabels = new()
        {
            [21] = "Door unlocked", [22] = "Door locked", [23] = "Exit button pressed", [24] = "Exit button released"
        };

        public static bool IsEntry(int code) => Methods.ContainsKey(code);
        public static bool IsDenied(int code) => DeniedLabels.ContainsKey(code);

        /// <summary>Report label of an access_event code; unknown codes fall back to the machine's own text.</summary>
        public static string LabelOf(int code, string? fallback)
        {
            if (Methods.TryGetValue(code, out var m)) return "Entry (" + m.ToLowerInvariant() + ")";
            if (DeniedLabels.TryGetValue(code, out var d)) return d;
            if (OtherLabels.TryGetValue(code, out var o)) return o;
            return string.IsNullOrWhiteSpace(fallback) ? "Event " + code : fallback.Trim();
        }

        /// <summary>A person = employee no; the name when there is none; entries need one of them.</summary>
        private static string PersonKey(RoomReportEventRow e) =>
            !string.IsNullOrWhiteSpace(e.EmployeeNo) ? e.EmployeeNo!.Trim() : (e.Name ?? "").Trim();

        public static RoomReportDto Build(IReadOnlyCollection<RoomReportMachineDto> machines, IEnumerable<RoomReportEventRow> rows,
            DateTime from, DateTime to, DateTime generatedAt)
        {
            var machineName = machines.ToDictionary(m => m.Id, m => m.Name);
            string Machine(RoomReportEventRow e) =>
                machineName.TryGetValue(e.DeviceId, out var n) ? n : (e.DeviceName ?? "").Trim();

            var all = rows.OrderBy(r => r.Time).ThenBy(r => r.DeviceId).ToList();
            var entryRows = all.Where(r => IsEntry(r.AccessEvent) && PersonKey(r).Length > 0).ToList();
            var deniedRows = all.Where(r => IsDenied(r.AccessEvent)).ToList();

            var report = new RoomReportDto
            {
                From = from.ToString(DateFormat),
                To = to.ToString(DateFormat),
                GeneratedAt = generatedAt.ToString(TimeFormat),
                Machines = machines.ToList()
            };

            report.Events = all.Select(e => new RoomReportEventDto
            {
                Time = e.Time.ToString(TimeFormat),
                Machine = Machine(e),
                Label = LabelOf(e.AccessEvent, e.Label),
                Name = (e.Name ?? "").Trim(),
                EmployeeNo = (e.EmployeeNo ?? "").Trim()
            }).ToList();

            report.Entries = entryRows.Select(e => new RoomReportEntryDto
            {
                Time = e.Time.ToString(TimeFormat),
                EmployeeNo = (e.EmployeeNo ?? "").Trim(),
                Name = (e.Name ?? "").Trim(),
                Method = Methods[e.AccessEvent],
                Machine = Machine(e)
            }).ToList();

            report.Denied = deniedRows.Select(e => new RoomReportDeniedDto
            {
                Time = e.Time.ToString(TimeFormat),
                Machine = Machine(e),
                Label = DeniedLabels[e.AccessEvent]
            }).ToList();

            // People: who, how often, which methods.
            report.People = entryRows.GroupBy(PersonKey).Select(g => new RoomReportPersonDto
            {
                EmployeeNo = (g.First().EmployeeNo ?? "").Trim(),
                Name = g.Select(x => (x.Name ?? "").Trim()).FirstOrDefault(n => n.Length > 0) ?? g.Key,
                DaysPresent = g.Select(x => x.Time.Date).Distinct().Count(),
                Entries = g.Count(),
                FirstDay = g.Min(x => x.Time).ToString(DateFormat),
                LastDay = g.Max(x => x.Time).ToString(DateFormat),
                Face = g.Count(x => Methods[x.AccessEvent] == "Face"),
                Fingerprint = g.Count(x => Methods[x.AccessEvent] == "Fingerprint"),
                Card = g.Count(x => Methods[x.AccessEvent] == "Card")
            }).OrderByDescending(p => p.Entries).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

            // Daily: one row per date per person; there is no exit identity, so check-out = last scan.
            report.Daily = entryRows.GroupBy(e => (e.Time.Date, Key: PersonKey(e)))
                .OrderBy(g => g.Key.Date).ThenBy(g => g.Key.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var first = g.Min(x => x.Time);
                    var last = g.Max(x => x.Time);
                    return new RoomReportDailyDto
                    {
                        Date = g.Key.Date.ToString(DateFormat),
                        EmployeeNo = (g.First().EmployeeNo ?? "").Trim(),
                        Name = g.Select(x => (x.Name ?? "").Trim()).FirstOrDefault(n => n.Length > 0) ?? g.Key.Key,
                        FirstEntry = first.ToString(TimeFormat),
                        LastScan = last.ToString(TimeFormat),
                        Hours = Math.Round((last - first).TotalHours, 2),
                        Entries = g.Count(),
                        Machines = g.Select(Machine).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                    };
                }).ToList();

            report.PerDay = all.GroupBy(e => e.Time.Date).OrderBy(g => g.Key).Select(g =>
            {
                var ent = g.Where(x => IsEntry(x.AccessEvent) && PersonKey(x).Length > 0).ToList();
                return new RoomReportPerDayDto
                {
                    Date = g.Key.ToString(DateFormat),
                    People = ent.Select(PersonKey).Distinct().Count(),
                    Entries = ent.Count,
                    Denied = g.Count(x => IsDenied(x.AccessEvent)),
                    ExitPresses = g.Count(x => x.AccessEvent == 23),
                    DoorUnlocks = g.Count(x => x.AccessEvent == 21),
                    FirstEntry = ent.Count > 0 ? ent.Min(x => x.Time).ToString(TimeFormat) : null,
                    LastEntry = ent.Count > 0 ? ent.Max(x => x.Time).ToString(TimeFormat) : null
                };
            }).ToList();

            report.Summary = new RoomReportSummaryDto
            {
                Events = all.Count,
                Entries = entryRows.Count,
                Denied = deniedRows.Count,
                People = report.People.Count,
                DaysActive = entryRows.Select(e => e.Time.Date).Distinct().Count(),
                ExitPresses = all.Count(e => e.AccessEvent == 23)
            };
            return report;
        }
    }
}
