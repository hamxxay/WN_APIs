namespace WorkNest.Application.DTOs.HikDevice
{
    /// <summary>
    /// One door/terminal event from WN_HIK_Events (archived from the machines by the HIK sync engine).
    /// Times are the terminal's local wall-clock, formatted "yyyy-MM-ddTHH:mm:ss" (no timezone).
    /// </summary>
    public class HikAccessEventDto
    {
        public long Id { get; set; }
        public int DeviceId { get; set; }
        public string? Device { get; set; }
        public string? EmployeeNo { get; set; }
        public string? Name { get; set; }
        public string? CardNo { get; set; }
        public int? EventCode { get; set; }
        public string Label { get; set; } = string.Empty;
        public bool IsDenied { get; set; }
        /// <summary>fingerprint | face | card | door | remote | other</summary>
        public string Method { get; set; } = "other";
        public string? Time { get; set; }
    }

    public class HikSyncActivityDto
    {
        public long Id { get; set; }
        public string? Action { get; set; }
        public bool Ok { get; set; }
        public string? Detail { get; set; }
        public string? Time { get; set; }
        public string? EmployeeName { get; set; }
        public string? DeviceName { get; set; }
    }

    public class HikExpiringMemberDto
    {
        public string? EmployeeNo { get; set; }
        public string? Name { get; set; }
        public string? ValidEnd { get; set; }
        public string Status { get; set; } = "expiring";
        public List<string> Devices { get; set; } = new();
    }

    public class HikAccessStatsDto
    {
        public int Devices { get; set; }
        public int DevicesOnline { get; set; }
        public int ActiveMembers { get; set; }
        public int ExpiredMembers { get; set; }
        public int Cards { get; set; }
        public int PendingSync { get; set; }
        public int TodayScans { get; set; }
        public int YesterdayScans { get; set; }
        public int TrendPct { get; set; }
        public int UniqueToday { get; set; }
        public int DeniedToday { get; set; }
    }

    public class HikAccessDashboardDto
    {
        public HikAccessStatsDto Stats { get; set; } = new();
        public List<HikDeviceDto> Devices { get; set; } = new();
        public int[] HourlyToday { get; set; } = new int[24];
        public string PeakHourLabel { get; set; } = "—";
        public int PeakHourCount { get; set; }
        public HikAccessEventDto? LastEvent { get; set; }
        public List<HikAccessEventDto> RecentEvents { get; set; } = new();
        public List<HikExpiringMemberDto> Expiring { get; set; } = new();
        public int ExpiringHorizonDays { get; set; }
    }

    public class HikCountItemDto
    {
        public string Name { get; set; } = string.Empty;
        public string? EmployeeNo { get; set; }
        public int Count { get; set; }
        public int Percent { get; set; }
    }

    public class HikMethodSplitDto
    {
        public int Fingerprint { get; set; }
        public int Face { get; set; }
        public int Card { get; set; }
        public int Door { get; set; }
        public int Other { get; set; }
    }

    public class HikDailyCountDto
    {
        public string Date { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class HikAccessAnalyticsDto
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public int TotalEvents { get; set; }
        public int UniquePeople { get; set; }
        public int Denied { get; set; }
        public int[] Hourly { get; set; } = new int[24];
        public string PeakHourLabel { get; set; } = "—";
        public int PeakHourCount { get; set; }
        public List<HikDailyCountDto> Daily { get; set; } = new();
        public List<HikCountItemDto> Doors { get; set; } = new();
        public List<HikCountItemDto> TopUsers { get; set; } = new();
        public HikMethodSplitDto Methods { get; set; } = new();
        public int DevicesCount { get; set; }
        public int DevicesOnline { get; set; }
    }

    public class HikUserAnalyticsDto
    {
        public string? EmployeeNo { get; set; }
        public string? Name { get; set; }
        public string? Room { get; set; }
        public string? CardNo { get; set; }
        public string? Status { get; set; }
        public string? ValidEnd { get; set; }
        public int TotalScans { get; set; }
        public int AllTimeScans { get; set; }
        public string? FirstScan { get; set; }
        public string? LastScan { get; set; }
        public string PeakHourLabel { get; set; } = "—";
        public int[] Hourly { get; set; } = new int[24];
        public List<HikCountItemDto> Doors { get; set; } = new();
        public List<HikAccessEventDto> RecentEvents { get; set; } = new();
    }
}
