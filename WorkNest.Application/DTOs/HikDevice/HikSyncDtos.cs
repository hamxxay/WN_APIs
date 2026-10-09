using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace WorkNest.Application.DTOs.HikDevice
{
    /// <summary>A machine as the sync jobs see it (WN_HIK_Devices + its group name).</summary>
    public class HikSyncDevice : HikDeviceConnection
    {
        public string? Group { get; set; }
        public bool IsEntrance => (Group ?? string.Empty).Trim().StartsWith("entrance", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One page of an ISAPI search (persons, events, cards) — raw device JSON objects, unchanged.</summary>
    public class HikSearchPage
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public int Total { get; set; }
        public List<JsonObject> List { get; set; } = new();
    }

    /// <summary>A person record pushed to a machine (UserInfo).</summary>
    public class HikPersonRecord
    {
        public string EmployeeNo { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ValidBegin { get; set; } = "2020-01-01T00:00:00";
        public string ValidEnd { get; set; } = "2037-12-31T23:59:59";
        public bool Enabled { get; set; } = true;
        /// <summary>localUIRight: true = admin (can open the terminal menu).</summary>
        public bool Admin { get; set; }
    }

    public class HikFingerprintTemplate
    {
        public int FingerPrintId { get; set; } = 1;
        public string FingerData { get; set; } = string.Empty;
    }

    /// <summary>Last run of one sync job (shown by GET api/hik/sync/status).</summary>
    public class HikSyncJobStatus
    {
        public string Job { get; set; } = string.Empty;
        public DateTime? LastStartedAt { get; set; }
        public DateTime? LastFinishedAt { get; set; }
        public bool? LastOk { get; set; }
        public string? LastResult { get; set; }
        public bool Running { get; set; }
    }

    /// <summary>Result of testing one machine (the "Test" button): reachable or not, with its details.</summary>
    public class HikDeviceTestResult
    {
        public int DeviceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Online { get; set; }
        public string? Error { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public string? FirmwareVersion { get; set; }
        public long ElapsedMs { get; set; }
    }

    /// <summary>ISAPI deviceInfo of a machine.</summary>
    public class HikDeviceInfo
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? DeviceName { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public string? FirmwareVersion { get; set; }
        public string? MacAddress { get; set; }
    }
}
