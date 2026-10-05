using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Read-only Access Dashboard / Activity Log / Analytics over the HIK tables
    /// (WN_HIK_Events, WN_HIK_SyncLog, WN_HIK_Devices). The HIK sync engine archives the
    /// terminal events; this service only reads them.
    /// </summary>
    public class HikAccessService : IHikAccessService
    {
        private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss";

        // Fallback labels when WN_HIK_EventCategories has no row (mirrors HIK eventCategories.js).
        private static readonly Dictionary<int, string> EventLabels = new()
        {
            [1] = "Entry authorized", [2] = "Card + password", [8] = "Card verify failed", [9] = "Unregistered card",
            [21] = "Door opened", [22] = "Door closed", [23] = "Door open timeout", [24] = "Door forced open (alarm)",
            [27] = "Remote unlock", [38] = "Fingerprint OK", [39] = "Fingerprint denied", [75] = "Face OK",
            [76] = "Face not recognized", [104] = "Face OK", [112] = "Entry denied (expired)"
        };

        private readonly IDbRepository _db;

        public HikAccessService(IDbRepository db)
        {
            _db = db;
        }

        public async Task<HikAccessDashboardDto> GetDashboardAsync(int expiringDays = 7)
        {
            expiringDays = Math.Clamp(expiringDays, 1, 60);
            var today = DateTime.Today;

            var statsTask = _db.GetHikAccessStatsDbAsync();
            var devicesTask = _db.GetHikDevicesAsync();
            var analyticsTask = _db.GetHikAccessAnalyticsDbAsync(today, today.AddDays(1));
            var recentTask = _db.GetHikAccessEventsDbAsync(today, today.AddDays(1), null, null, null, 10);
            var expiringTask = _db.GetHikExpiringDbAsync(expiringDays);
            await Task.WhenAll(statsTask, devicesTask, analyticsTask, recentTask, expiringTask);

            var s = statsTask.Result;
            var stats = new HikAccessStatsDto
            {
                Devices = Int(s, "devices"),
                DevicesOnline = Int(s, "devicesOnline"),
                ActiveMembers = Int(s, "activeMembers"),
                ExpiredMembers = Int(s, "expiredMembers"),
                Cards = Int(s, "cards"),
                PendingSync = Int(s, "pendingSync"),
                TodayScans = Int(s, "todayScans"),
                YesterdayScans = Int(s, "yesterdayScans"),
                UniqueToday = Int(s, "uniqueToday"),
                DeniedToday = Int(s, "deniedToday")
            };
            stats.TrendPct = stats.YesterdayScans > 0
                ? (int)Math.Round((stats.TodayScans - stats.YesterdayScans) * 100.0 / stats.YesterdayScans)
                : (stats.TodayScans > 0 ? 100 : 0);

            var hourly = Hourly(analyticsTask.Result[0]);
            var (peakLabel, peakCount) = Peak(hourly);
            var recent = recentTask.Result.Select(MapEvent).ToList();

            return new HikAccessDashboardDto
            {
                Stats = stats,
                Devices = devicesTask.Result.ToList(),
                HourlyToday = hourly,
                PeakHourLabel = peakLabel,
                PeakHourCount = peakCount,
                LastEvent = recent.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Name)),
                RecentEvents = recent,
                Expiring = GroupExpiring(expiringTask.Result),
                ExpiringHorizonDays = expiringDays
            };
        }

        public async Task<IEnumerable<HikAccessEventDto>> GetEventsAsync(DateTime? from, DateTime? to, int? deviceId, string? employeeNo, string? name, int limit = 500)
        {
            var rows = await _db.GetHikAccessEventsDbAsync(from, to, deviceId, employeeNo, name, Math.Clamp(limit, 1, 2000));
            return rows.Select(MapEvent).ToList();
        }

        public async Task<IEnumerable<HikSyncActivityDto>> GetSyncActivityAsync(int limit = 200)
        {
            var rows = await _db.GetHikSyncActivityDbAsync(Math.Clamp(limit, 1, 1000));
            return rows.Select(r => new HikSyncActivityDto
            {
                Id = Long(r, "id"),
                Action = Str(r, "action"),
                Ok = Bool(r, "ok"),
                Detail = Str(r, "detail"),
                Time = Time(r, "ts"),
                EmployeeName = Str(r, "employee_name"),
                DeviceName = Str(r, "device_name")
            }).ToList();
        }

        public async Task<HikAccessAnalyticsDto> GetAnalyticsAsync(DateTime from, DateTime to)
        {
            var (start, end) = Range(from, to);
            var setsTask = _db.GetHikAccessAnalyticsDbAsync(start, end);
            var statsTask = _db.GetHikAccessStatsDbAsync();
            await Task.WhenAll(setsTask, statsTask);
            var sets = setsTask.Result;

            var hourly = Hourly(sets[0]);
            var (peakLabel, peakCount) = Peak(hourly);
            var totals = sets[3].FirstOrDefault() ?? new Dictionary<string, object?>();

            return new HikAccessAnalyticsDto
            {
                From = start.ToString("yyyy-MM-dd"),
                To = end.AddDays(-1).ToString("yyyy-MM-dd"),
                TotalEvents = Int(totals, "total"),
                UniquePeople = Int(totals, "uniquePeople"),
                Denied = Int(totals, "denied"),
                Hourly = hourly,
                PeakHourLabel = peakLabel,
                PeakHourCount = peakCount,
                Daily = sets[4].Select(r => new HikDailyCountDto
                {
                    Date = r["d"] is DateTime d ? d.ToString("yyyy-MM-dd") : "",
                    Count = Int(r, "cnt")
                }).ToList(),
                Doors = WithPercent(sets[1].Select(r => new HikCountItemDto { Name = Str(r, "name") ?? "", Count = Int(r, "cnt") })),
                TopUsers = WithPercent(sets[2].Select(r => new HikCountItemDto
                {
                    EmployeeNo = Str(r, "employee_no"),
                    Name = Str(r, "name") ?? "",
                    Count = Int(r, "cnt")
                })),
                Methods = new HikMethodSplitDto
                {
                    Fingerprint = Int(totals, "fingerprint"),
                    Face = Int(totals, "face"),
                    Card = Int(totals, "card"),
                    Door = Int(totals, "door"),
                    Other = Math.Max(0, Int(totals, "total") - Int(totals, "fingerprint") - Int(totals, "face") - Int(totals, "card") - Int(totals, "door"))
                },
                DevicesCount = Int(statsTask.Result, "devices"),
                DevicesOnline = Int(statsTask.Result, "devicesOnline")
            };
        }

        public async Task<HikUserAnalyticsDto> GetUserAnalyticsAsync(string? employeeNo, string? name, DateTime from, DateTime to)
        {
            var (start, end) = Range(from, to);
            var setsTask = _db.GetHikUserAnalyticsDbAsync(employeeNo, name, start, end);
            var recentTask = _db.GetHikAccessEventsDbAsync(start, end, null, employeeNo, name, 20);
            await Task.WhenAll(setsTask, recentTask);
            var sets = setsTask.Result;

            var profile = sets[0].FirstOrDefault() ?? new Dictionary<string, object?>();
            var totals = sets[3].FirstOrDefault() ?? new Dictionary<string, object?>();
            var hourly = Hourly(sets[2]);
            var (peakLabel, peakCount) = Peak(hourly);

            return new HikUserAnalyticsDto
            {
                EmployeeNo = Str(profile, "employee_no") ?? employeeNo,
                Name = Str(profile, "name") ?? name,
                Room = Str(profile, "room"),
                CardNo = Str(profile, "card_no") ?? Str(totals, "cardNo"),
                Status = Str(profile, "status"),
                ValidEnd = Time(profile, "valid_end"),
                TotalScans = Int(totals, "total"),
                AllTimeScans = Int(sets[4].FirstOrDefault() ?? new Dictionary<string, object?>(), "total"),
                FirstScan = Time(totals, "firstScan"),
                LastScan = Time(totals, "lastScan"),
                PeakHourLabel = peakCount > 0 ? $"{peakLabel} ({peakCount} scans)" : "—",
                Hourly = hourly,
                Doors = WithPercent(sets[1].Select(r => new HikCountItemDto { Name = Str(r, "name") ?? "", Count = Int(r, "cnt") })),
                RecentEvents = recentTask.Result.Select(MapEvent).ToList()
            };
        }

        // ---- Mapping helpers --------------------------------------------------------

        private static HikAccessEventDto MapEvent(IDictionary<string, object?> r)
        {
            var code = r.TryGetValue("access_event", out var c) && c != null ? Convert.ToInt32(c) : (int?)null;
            var cardNo = Str(r, "card_no");
            var label = Str(r, "label");
            if (string.IsNullOrWhiteSpace(label))
                label = code.HasValue && EventLabels.TryGetValue(code.Value, out var l) ? l : $"Event {code}";

            return new HikAccessEventDto
            {
                Id = Long(r, "id"),
                DeviceId = Int(r, "device_id"),
                Device = Str(r, "device_name"),
                EmployeeNo = Str(r, "employee_no"),
                Name = Str(r, "name"),
                CardNo = cardNo,
                EventCode = code,
                Label = label!,
                IsDenied = Int(r, "is_denied") == 1,
                Method = MethodFor(code, cardNo),
                Time = Time(r, "event_time")
            };
        }

        private static string MethodFor(int? code, string? cardNo) => code switch
        {
            38 or 39 => "fingerprint",
            75 or 76 or 104 => "face",
            _ when !string.IsNullOrWhiteSpace(cardNo) => "card",
            >= 21 and <= 26 or 31 => "door",
            27 => "remote",
            _ => "other"
        };

        private static List<HikExpiringMemberDto> GroupExpiring(IEnumerable<IDictionary<string, object?>> rows)
        {
            var now = DateTime.Now;
            return rows
                .GroupBy(r => $"{Str(r, "employee_no")}||{(Str(r, "name") ?? "").Trim().ToLowerInvariant()}")
                .Select(g =>
                {
                    var first = g.First();
                    var end = first["valid_end"] as DateTime?;
                    return new HikExpiringMemberDto
                    {
                        EmployeeNo = Str(first, "employee_no"),
                        Name = Str(first, "name"),
                        ValidEnd = end?.ToString(TimeFormat),
                        Status = end.HasValue && end.Value < now ? "expired" : "expiring",
                        Devices = g.Select(r => Str(r, "device") ?? "").Where(d => d != "").Distinct().ToList()
                    };
                })
                .ToList();
        }

        private static int[] Hourly(IEnumerable<IDictionary<string, object?>> rows)
        {
            var hourly = new int[24];
            foreach (var r in rows)
            {
                var hr = Int(r, "hr");
                if (hr is >= 0 and < 24) hourly[hr] = Int(r, "cnt");
            }
            return hourly;
        }

        private static (string Label, int Count) Peak(int[] hourly)
        {
            var max = hourly.Max();
            if (max == 0) return ("—", 0);
            var hr = Array.IndexOf(hourly, max);
            return ($"{Hour12(hr)} – {Hour12((hr + 1) % 24)}", max);
        }

        private static string Hour12(int hr) => new DateTime(2000, 1, 1, hr, 0, 0).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);

        private static List<HikCountItemDto> WithPercent(IEnumerable<HikCountItemDto> items)
        {
            var list = items.ToList();
            var total = list.Sum(i => i.Count);
            foreach (var i in list) i.Percent = total > 0 ? (int)Math.Round(i.Count * 100.0 / total) : 0;
            return list;
        }

        /// <summary>Inclusive date range → [from 00:00, to+1 00:00).</summary>
        private static (DateTime Start, DateTime End) Range(DateTime from, DateTime to)
        {
            var start = from.Date;
            var end = to.Date < start ? start : to.Date;
            if ((end - start).TotalDays > 366) start = end.AddDays(-366);
            return (start, end.AddDays(1));
        }

        private static string? Str(IDictionary<string, object?> r, string key) =>
            r.TryGetValue(key, out var v) && v != null ? Convert.ToString(v) : null;

        private static int Int(IDictionary<string, object?> r, string key) =>
            r.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v) : 0;

        private static long Long(IDictionary<string, object?> r, string key) =>
            r.TryGetValue(key, out var v) && v != null ? Convert.ToInt64(v) : 0;

        private static bool Bool(IDictionary<string, object?> r, string key) =>
            r.TryGetValue(key, out var v) && v != null && Convert.ToBoolean(v);

        private static string? Time(IDictionary<string, object?> r, string key) =>
            r.TryGetValue(key, out var v) && v is DateTime d ? d.ToString(TimeFormat) : null;
    }
}
