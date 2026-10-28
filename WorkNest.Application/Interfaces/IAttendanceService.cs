using WorkNest.Application.DTOs.Attendance;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Customer attendance (off until staff turn it on per customer). Days are computed on request from the
    /// successful scans of the customer's employees on the room machines of the customer's booked rooms
    /// (<see cref="WorkNest.Application.Services.AttendanceCalculator"/>). Throws
    /// <see cref="AttendanceNotInstalledException"/> until the ATT_01 database update is run.
    /// </summary>
    public interface IAttendanceService
    {
        /// <summary>The customer whose main account is the user (employees with a login get null).</summary>
        Task<AttendanceCustomerRow?> GetCustomerByUserAsync(int userId);
        Task<AttendanceCustomerRow?> GetCustomerAsync(int customerId);

        Task<AttendanceOutcome> SetEnabledAsync(int customerId, bool enabled, string? by);
        Task<List<AttendanceCustomerDto>> GetCustomersAsync(IReadOnlyCollection<int>? locationIds);

        /// <summary>Report from .. to ("yyyy-MM-dd"; default this month up to today), at most 62 days.</summary>
        Task<AttendanceOutcome> GetReportAsync(AttendanceCustomerRow customer, string? from, string? to, int? personId);
        Task<List<AttendanceEmployeeDto>> GetEmployeesAsync(int customerId);

        Task<List<AttendanceShiftDto>> GetShiftsAsync(int customerId);
        Task<AttendanceOutcome> SaveShiftAsync(int customerId, int? id, AttendanceShiftInput? input, string? by);
        Task<AttendanceOutcome> DeleteShiftAsync(int customerId, int id, string? by);
        Task<AttendanceOutcome> SetEmployeeShiftAsync(int customerId, int personId, int? shiftId, string? by);

        /// <summary>Holidays of a year; null = all, by date.</summary>
        Task<List<AttendanceHolidayDto>> GetHolidaysAsync(int customerId, int? year);
        Task<AttendanceOutcome> AddHolidayAsync(int customerId, AttendanceHolidayInput? input, string? by);
        Task<AttendanceOutcome> DeleteHolidayAsync(int customerId, int id);
    }
}
