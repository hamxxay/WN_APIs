using System;
using System.Collections.Generic;

namespace WorkNest.Application.DTOs.Attendance
{
    // ---- API shapes (Database/Attendance CONTRACT: camelCase inside ApiResponse.data) ----------------------------------
    // Dates are "yyyy-MM-dd", times "HH:mm", scan times "yyyy-MM-ddTHH:mm:ss" (machine time, Pakistan), as strings so the
    // JSON never carries a time zone.

    public class AttendanceStatusDto
    {
        public bool Enabled { get; set; }
        public int? CustomerId { get; set; }
        public string? CustomerName { get; set; }
    }

    public class AttendanceShiftDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string StartTime { get; set; } = "09:00";
        public string EndTime { get; set; } = "18:00";
        public int GraceMinutes { get; set; }
        public int? HalfDayMinutes { get; set; }
        public List<string> WorkDays { get; set; } = new();
        public bool IsDefault { get; set; }
        public bool CrossesMidnight { get; set; }
        public int EmployeeCount { get; set; }
    }

    public class AttendanceShiftInput
    {
        public string? Name { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public int GraceMinutes { get; set; }
        public int? HalfDayMinutes { get; set; }
        public List<string>? WorkDays { get; set; }
        public bool IsDefault { get; set; }
    }

    public class AttendanceShiftAssignInput
    {
        public int? ShiftId { get; set; }
    }

    public class AttendanceHolidayDto
    {
        public int Id { get; set; }
        public string Date { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class AttendanceHolidayInput
    {
        public string? Date { get; set; }
        public string? Name { get; set; }
    }

    public class AttendanceEnableInput
    {
        public bool Enabled { get; set; }
    }

    public class AttendanceEmployeeDto
    {
        public int PersonId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IdNumberMasked { get; set; } = string.Empty;
        public List<string> Rooms { get; set; } = new();
        public int? ShiftId { get; set; }
        public string? ShiftName { get; set; }
        public bool UsesDefaultShift { get; set; }
        public bool Active { get; set; }
        public bool Enrolled { get; set; }
    }

    public class AttendanceDayDto
    {
        public int PersonId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string? CheckIn { get; set; }
        public string? CheckOut { get; set; }
        public int? WorkedMinutes { get; set; }
        public int Scans { get; set; }
        public string Status { get; set; } = AttendanceStatuses.Present;
        public int LateMinutes { get; set; }
        public int EarlyLeaveMinutes { get; set; }
        public int OvertimeMinutes { get; set; }
        public bool OnHoliday { get; set; }
        public bool OnOffDay { get; set; }
        public string? ShiftName { get; set; }
        public string? Room { get; set; }
    }

    public class AttendanceSummaryDto
    {
        public int PersonId { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>Days attended (any scan): Present + Late + HalfDay + MissingOut.</summary>
        public int Present { get; set; }
        public int Late { get; set; }
        public int HalfDay { get; set; }
        public int MissingOut { get; set; }
        public int Absent { get; set; }
        public int Holidays { get; set; }
        public int OffDays { get; set; }
        public int WorkedMinutes { get; set; }
        public int OvertimeMinutes { get; set; }
        public int LateMinutes { get; set; }
    }

    public class AttendanceReportDto
    {
        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public List<AttendanceDayDto> Days { get; set; } = new();
        public List<AttendanceSummaryDto> Summary { get; set; } = new();
    }

    public class AttendanceCustomerDto
    {
        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public bool Enabled { get; set; }
        public int EmployeeCount { get; set; }
    }

    public static class AttendanceStatuses
    {
        public const string Present = "Present";
        public const string Late = "Late";
        public const string HalfDay = "HalfDay";
        public const string MissingOut = "MissingOut";
        public const string Absent = "Absent";
        public const string Holiday = "Holiday";
        public const string OffDay = "OffDay";
        public const string Pending = "Pending";
    }

    // ---- Rows of the dbo.WN_Attendance_* procedures (ATT_01) ---------------------------------------------------------

    public class AttendanceCustomerRow
    {
        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public bool Enabled { get; set; }
        public int EmployeeCount { get; set; }
    }

    public class AttendancePersonRow
    {
        public int PersonId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IdType { get; set; } = string.Empty;
        public string IdNumber { get; set; } = string.Empty;
        /// <summary>WN_HIK_PersonMap.MachineID = WN_HIK_Events.employee_no; null = not enrolled.</summary>
        public string? MachineId { get; set; }
    }

    public class AttendanceAssignmentRow
    {
        public int PersonId { get; set; }
        public int BookingDetailId { get; set; }
        public string? SpaceName { get; set; }
        public DateTime AssignedFrom { get; set; }
        /// <summary>Last day on the room (null = open).</summary>
        public DateTime? AssignedTo { get; set; }
    }

    public class AttendanceRoomRow
    {
        public int BookingDetailId { get; set; }
        public int BookingId { get; set; }
        public string? SpaceName { get; set; }
        public string? SpaceCode { get; set; }
        public string? CategoryCode { get; set; }
        public int? LocationId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    public class AttendanceScanRow
    {
        public int DeviceId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public DateTime EventTime { get; set; }
    }

    public class AttendanceShiftRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int GraceMinutes { get; set; }
        public int? HalfDayMinutes { get; set; }
        public string WorkDays { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
    }

    public class AttendanceHolidayRow
    {
        public int Id { get; set; }
        public DateTime HolidayDate { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Result of a service call: HTTP status + ApiResponse message / data (as BookingEmployeeOutcome).</summary>
    public class AttendanceOutcome
    {
        public int StatusCode { get; init; } = 200;
        public bool IsSuccessful => StatusCode is >= 200 and < 300;
        public string Message { get; init; } = "Success";
        public object? Data { get; init; }

        public static AttendanceOutcome Ok(object? data, string message = "Success") => new() { Data = data, Message = message };
        public static AttendanceOutcome Fail(int status, string message) => new() { StatusCode = status, Message = message };
    }
}
