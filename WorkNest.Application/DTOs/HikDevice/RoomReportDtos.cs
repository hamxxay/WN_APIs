using System.Text.Json.Serialization;

namespace WorkNest.Application.DTOs.HikDevice
{
    /// <summary>One archived door event (WN_HIK_Events) as the room report needs it; Time is the machine's local time.</summary>
    public class RoomReportEventRow
    {
        public int DeviceId { get; set; }
        public string? DeviceName { get; set; }
        public string? EmployeeNo { get; set; }
        public string? Name { get; set; }
        public int AccessEvent { get; set; }
        /// <summary>Category label or the machine's access_event_details; only used for codes the report does not know.</summary>
        public string? Label { get; set; }
        public DateTime Time { get; set; }
    }

    public class RoomReportMachineDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("locationName")] public string? LocationName { get; set; }
    }

    public class RoomReportSummaryDto
    {
        [JsonPropertyName("events")] public int Events { get; set; }
        [JsonPropertyName("entries")] public int Entries { get; set; }
        [JsonPropertyName("denied")] public int Denied { get; set; }
        [JsonPropertyName("people")] public int People { get; set; }
        [JsonPropertyName("daysActive")] public int DaysActive { get; set; }
        [JsonPropertyName("exitPresses")] public int ExitPresses { get; set; }
    }

    public class RoomReportPersonDto
    {
        [JsonPropertyName("employeeNo")] public string EmployeeNo { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("daysPresent")] public int DaysPresent { get; set; }
        [JsonPropertyName("entries")] public int Entries { get; set; }
        [JsonPropertyName("firstDay")] public string FirstDay { get; set; } = string.Empty;
        [JsonPropertyName("lastDay")] public string LastDay { get; set; } = string.Empty;
        [JsonPropertyName("face")] public int Face { get; set; }
        [JsonPropertyName("fingerprint")] public int Fingerprint { get; set; }
        [JsonPropertyName("card")] public int Card { get; set; }
    }

    public class RoomReportDailyDto
    {
        [JsonPropertyName("date")] public string Date { get; set; } = string.Empty;
        [JsonPropertyName("employeeNo")] public string EmployeeNo { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("firstEntry")] public string FirstEntry { get; set; } = string.Empty;
        [JsonPropertyName("lastScan")] public string LastScan { get; set; } = string.Empty;
        [JsonPropertyName("hours")] public double Hours { get; set; }
        [JsonPropertyName("entries")] public int Entries { get; set; }
        [JsonPropertyName("machines")] public List<string> Machines { get; set; } = new();
    }

    public class RoomReportEntryDto
    {
        [JsonPropertyName("time")] public string Time { get; set; } = string.Empty;
        [JsonPropertyName("employeeNo")] public string EmployeeNo { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("method")] public string Method { get; set; } = string.Empty;
        [JsonPropertyName("machine")] public string Machine { get; set; } = string.Empty;
    }

    public class RoomReportDeniedDto
    {
        [JsonPropertyName("time")] public string Time { get; set; } = string.Empty;
        [JsonPropertyName("machine")] public string Machine { get; set; } = string.Empty;
        [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
    }

    public class RoomReportPerDayDto
    {
        [JsonPropertyName("date")] public string Date { get; set; } = string.Empty;
        [JsonPropertyName("people")] public int People { get; set; }
        [JsonPropertyName("entries")] public int Entries { get; set; }
        [JsonPropertyName("denied")] public int Denied { get; set; }
        [JsonPropertyName("exitPresses")] public int ExitPresses { get; set; }
        [JsonPropertyName("doorUnlocks")] public int DoorUnlocks { get; set; }
        [JsonPropertyName("firstEntry")] public string? FirstEntry { get; set; }
        [JsonPropertyName("lastEntry")] public string? LastEntry { get; set; }
    }

    public class RoomReportEventDto
    {
        [JsonPropertyName("time")] public string Time { get; set; } = string.Empty;
        [JsonPropertyName("machine")] public string Machine { get; set; } = string.Empty;
        [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("employeeNo")] public string EmployeeNo { get; set; } = string.Empty;
    }

    public class RoomReportDto
    {
        [JsonPropertyName("from")] public string From { get; set; } = string.Empty;
        [JsonPropertyName("to")] public string To { get; set; } = string.Empty;
        [JsonPropertyName("generatedAt")] public string GeneratedAt { get; set; } = string.Empty;
        [JsonPropertyName("machines")] public List<RoomReportMachineDto> Machines { get; set; } = new();
        [JsonPropertyName("summary")] public RoomReportSummaryDto Summary { get; set; } = new();
        [JsonPropertyName("people")] public List<RoomReportPersonDto> People { get; set; } = new();
        [JsonPropertyName("daily")] public List<RoomReportDailyDto> Daily { get; set; } = new();
        [JsonPropertyName("entries")] public List<RoomReportEntryDto> Entries { get; set; } = new();
        [JsonPropertyName("denied")] public List<RoomReportDeniedDto> Denied { get; set; } = new();
        [JsonPropertyName("perDay")] public List<RoomReportPerDayDto> PerDay { get; set; } = new();
        [JsonPropertyName("events")] public List<RoomReportEventDto> Events { get; set; } = new();
    }
}
