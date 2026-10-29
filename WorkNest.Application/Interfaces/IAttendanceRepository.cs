using WorkNest.Application.DTOs.Attendance;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Customer attendance settings and raw data through the dbo.WN_Attendance_* procedures (Database script
    /// ATT_01_customer_attendance). Throws <see cref="AttendanceNotInstalledException"/> until that script has run.
    /// </summary>
    public interface IAttendanceRepository
    {
        /// <summary>The customer whose main account (WN_Customers.UserId) is the user; null = none.</summary>
        Task<AttendanceCustomerRow?> GetCustomerByUserAsync(int userId);
        Task<AttendanceCustomerRow?> GetCustomerAsync(int customerId);
        /// <summary>Attendance on / off; returns the customer (null = no such customer).</summary>
        Task<AttendanceCustomerRow?> SetEnabledAsync(int customerId, bool enabled, string? by);
        /// <summary>Customers with attendance on or with employees; locationIds null = every location.</summary>
        Task<List<AttendanceCustomerRow>> GetCustomersAsync(IReadOnlyCollection<int>? locationIds);

        /// <summary>Everyone ever on one of the customer's rooms, and their room assignments.</summary>
        Task<(List<AttendancePersonRow> People, List<AttendanceAssignmentRow> Assignments)> GetPeopleAsync(int customerId);
        /// <summary>The customer's booked rooms overlapping from .. to (null = no limit).</summary>
        Task<List<AttendanceRoomRow>> GetRoomsAsync(int customerId, DateTime? from, DateTime? to);
        /// <summary>Successful scans of the machine ids on the devices, from &lt;= time &lt; to (machine time).</summary>
        Task<List<AttendanceScanRow>> GetScansAsync(IReadOnlyCollection<int> deviceIds, IReadOnlyCollection<string> employeeNos, DateTime from, DateTime to);

        /// <summary>The customer's shifts and each employee's own shift (PersonId -> ShiftId).</summary>
        Task<(List<AttendanceShiftRow> Shifts, Dictionary<int, int> Assignments)> GetShiftsAsync(int customerId);
        /// <summary>Adds (id null) or changes a shift; returns its id, null = not a shift of this customer.</summary>
        Task<int?> SaveShiftAsync(int customerId, int? id, string name, TimeSpan start, TimeSpan end, int graceMinutes,
            int? halfDayMinutes, string workDays, bool isDefault, string? by);
        Task<int> DeleteShiftAsync(int customerId, int id, string? by);
        /// <summary>ok / person_not_found / shift_not_found.</summary>
        Task<string> SetEmployeeShiftAsync(int customerId, int personId, int? shiftId, string? by);

        Task<List<AttendanceHolidayRow>> GetHolidaysAsync(int customerId, DateTime? from, DateTime? to);
        /// <summary>(ok, id) or (exists, null).</summary>
        Task<(string Result, int? Id)> AddHolidayAsync(int customerId, DateTime date, string name, string? by);
        Task<int> DeleteHolidayAsync(int customerId, int id);
    }

    /// <summary>The ATT_01 database update (attendance tables + procedures) has not been run yet.</summary>
    public class AttendanceNotInstalledException : Exception
    {
        public const string UserMessage = "Attendance is not installed yet (run ATT_01)";

        public AttendanceNotInstalledException(Exception inner) : base(UserMessage, inner) { }
    }
}
