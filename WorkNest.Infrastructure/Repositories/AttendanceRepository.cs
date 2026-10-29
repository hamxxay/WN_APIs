using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Attendance;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Customer attendance through the dbo.WN_Attendance_* procedures (Database script ATT_01_customer_attendance).
    /// SQL errors 208 / 2812 / 207 (table / procedure / column missing) mean the script has not been run yet: they
    /// become <see cref="AttendanceNotInstalledException"/>.
    /// </summary>
    public class AttendanceRepository : IAttendanceRepository
    {
        private readonly string _connectionString;

        public AttendanceRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        // ---- Customer and switch ----

        public Task<AttendanceCustomerRow?> GetCustomerByUserAsync(int userId) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_Attendance_GetCustomerByUser", c);
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                return await ReadCustomerAsync(cmd);
            });

        public Task<AttendanceCustomerRow?> GetCustomerAsync(int customerId) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_Attendance_GetCustomer", c);
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
                return await ReadCustomerAsync(cmd);
            });

        public Task<AttendanceCustomerRow?> SetEnabledAsync(int customerId, bool enabled, string? by) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_Attendance_SetEnabled", c);
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
                cmd.Parameters.Add("@Enabled", SqlDbType.Bit).Value = enabled;
                cmd.Parameters.Add("@By", SqlDbType.NVarChar, 256).Value = (object?)by ?? DBNull.Value;
                return await ReadCustomerAsync(cmd);
            });

        public Task<List<AttendanceCustomerRow>> GetCustomersAsync(IReadOnlyCollection<int>? locationIds) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_Attendance_GetCustomers", c);
                cmd.Parameters.Add("@LocationIds", SqlDbType.NVarChar, -1).Value =
                    locationIds == null ? DBNull.Value : string.Join(",", locationIds);
                var list = new List<AttendanceCustomerRow>();
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    list.Add(new AttendanceCustomerRow
                    {
                        CustomerId = Int(r, "CustomerId") ?? 0,
                        CustomerName = Str(r, "CustomerName"),
                        Enabled = Bool(r, "AttendanceEnabled"),
                        EmployeeCount = Int(r, "EmployeeCount") ?? 0
                    });
                }
                return list;
            });

        // ---- People, rooms, scans ----

        public Task<(List<AttendancePersonRow> People, List<AttendanceAssignmentRow> Assignments)> GetPeopleAsync(int customerId) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_Attendance_GetPeople", c);
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
                var people = new List<AttendancePersonRow>();
                var assignments = new List<AttendanceAssignmentRow>();
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    people.Add(new AttendancePersonRow
                    {
                        PersonId = Int(r, "PersonId") ?? 0,
                        Name = Str(r, "Name") ?? "",
                        IdType = Str(r, "IdType") ?? "",
                        IdNumber = Str(r, "IdNumber") ?? "",
                        MachineId = Str(r, "MachineID")
                    });
                }
                if (await r.NextResultAsync())
                {
                    while (await r.ReadAsync())
                    {
                        assignments.Add(new AttendanceAssignmentRow
                        {
                            PersonId = Int(r, "PersonId") ?? 0,
                            BookingDetailId = Int(r, "BookingDetailId") ?? 0,
                            SpaceName = Str(r, "SpaceName"),
                            AssignedFrom = Date(r, "AssignedFrom") ?? DateTime.MinValue,
                            AssignedTo = Date(r, "AssignedTo")
                        });
                    }
                }
                return (people, assignments);
            });

        public Task<List<AttendanceRoomRow>> GetRoomsAsync(int customerId, DateTime? from, DateTime? to) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_Attendance_GetRooms", c);
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
                cmd.Parameters.Add("@From", SqlDbType.Date).Value = (object?)from?.Date ?? DBNull.Value;
                cmd.Parameters.Add("@To", SqlDbType.Date).Value = (object?)to?.Date ?? DBNull.Value;
                var list = new List<AttendanceRoomRow>();
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    list.Add(new AttendanceRoomRow
                    {
                        BookingDetailId = Int(r, "BookingDetailId") ?? 0,
                        BookingId = Int(r, "BookingId") ?? 0,
                        SpaceName = Str(r, "SpaceName"),
                        SpaceCode = Str(r, "SpaceCode"),
                        CategoryCode = Str(r, "CategoryCode"),
                        LocationId = Int(r, "LocationId"),
                        StartDate = Date(r, "StartDate"),
                        EndDate = Date(r, "EndDate")
                    });
                }
                return list;
            });

        public Task<List<AttendanceScanRow>> GetScansAsync(IReadOnlyCollection<int> deviceIds, IReadOnlyCollection<string> employeeNos, DateTime from, DateTime to) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_Attendance_GetScans", c);
                cmd.Parameters.Add("@DeviceIds", SqlDbType.NVarChar, -1).Value = string.Join(",", deviceIds);
                cmd.Parameters.Add("@EmployeeNos", SqlDbType.NVarChar, -1).Value = string.Join(",", employeeNos.Select(e => e.Replace(",", "")));
                cmd.Parameters.Add("@From", SqlDbType.DateTime2).Value = from;
                cmd.Parameters.Add("@To", SqlDbType.DateTime2).Value = to;
                cmd.CommandTimeout = 120;
                var list = new List<AttendanceScanRow>();
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    list.Add(new AttendanceScanRow
                    {
                        DeviceId = Int(r, "DeviceId") ?? 0,
                        EmployeeNo = Str(r, "EmployeeNo") ?? "",
                        EventTime = Date(r, "EventTime") ?? DateTime.MinValue
                    });
                }
                return list;
            });

        // ---- Shifts ----

        public Task<(List<AttendanceShiftRow> Shifts, Dictionary<int, int> Assignments)> GetShiftsAsync(int customerId) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_Attendance_Shifts_GetList", c);
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
                var shifts = new List<AttendanceShiftRow>();
                var assignments = new Dictionary<int, int>();
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    shifts.Add(new AttendanceShiftRow
                    {
                        Id = Int(r, "Id") ?? 0,
                        Name = Str(r, "Name") ?? "",
                        StartTime = r["StartTime"] is TimeSpan s ? s : TimeSpan.Zero,
                        EndTime = r["EndTime"] is TimeSpan e ? e : TimeSpan.Zero,
                        GraceMinutes = Int(r, "GraceMinutes") ?? 0,
                        HalfDayMinutes = Int(r, "HalfDayMinutes"),
                        WorkDays = Str(r, "WorkDays") ?? "",
                        IsDefault = Bool(r, "IsDefault")
                    });
                }
                if (await r.NextResultAsync())
                {
                    while (await r.ReadAsync())
                        assignments[Int(r, "PersonId") ?? 0] = Int(r, "ShiftId") ?? 0;
                }
                return (shifts, assignments);
            });

        public Task<int?> SaveShiftAsync(int customerId, int? id, string name, TimeSpan start, TimeSpan end, int graceMinutes,
            int? halfDayMinutes, string workDays, bool isDefault, string? by) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_Attendance_Shifts_Save", c);
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
                cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = name;
                cmd.Parameters.Add("@StartTime", SqlDbType.Time).Value = start;
                cmd.Parameters.Add("@EndTime", SqlDbType.Time).Value = end;
                cmd.Parameters.Add("@GraceMinutes", SqlDbType.Int).Value = graceMinutes;
                cmd.Parameters.Add("@HalfDayMinutes", SqlDbType.Int).Value = (object?)halfDayMinutes ?? DBNull.Value;
                cmd.Parameters.Add("@WorkDays", SqlDbType.NVarChar, 40).Value = workDays;
                cmd.Parameters.Add("@IsDefault", SqlDbType.Bit).Value = isDefault;
                cmd.Parameters.Add("@By", SqlDbType.NVarChar, 256).Value = (object?)by ?? DBNull.Value;
                var v = await cmd.ExecuteScalarAsync();
                return v is null or DBNull ? (int?)null : Convert.ToInt32(v);
            });

        public Task<int> DeleteShiftAsync(int customerId, int id, string? by) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_Attendance_Shifts_Delete", c);
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
                cmd.Parameters.Add("@By", SqlDbType.NVarChar, 256).Value = (object?)by ?? DBNull.Value;
                return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
            });

        public Task<string> SetEmployeeShiftAsync(int customerId, int personId, int? shiftId, string? by) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_Attendance_ShiftAssignments_Set", c);
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
                cmd.Parameters.Add("@PersonId", SqlDbType.Int).Value = personId;
                cmd.Parameters.Add("@ShiftId", SqlDbType.Int).Value = (object?)shiftId ?? DBNull.Value;
                cmd.Parameters.Add("@By", SqlDbType.NVarChar, 256).Value = (object?)by ?? DBNull.Value;
                return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? "error";
            });

        // ---- Holidays ----

        public Task<List<AttendanceHolidayRow>> GetHolidaysAsync(int customerId, DateTime? from, DateTime? to) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_Attendance_Holidays_GetList", c);
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
                cmd.Parameters.Add("@From", SqlDbType.Date).Value = (object?)from?.Date ?? DBNull.Value;
                cmd.Parameters.Add("@To", SqlDbType.Date).Value = (object?)to?.Date ?? DBNull.Value;
                var list = new List<AttendanceHolidayRow>();
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    list.Add(new AttendanceHolidayRow
                    {
                        Id = Int(r, "Id") ?? 0,
                        HolidayDate = Date(r, "HolidayDate") ?? DateTime.MinValue,
                        Name = Str(r, "Name") ?? ""
                    });
                }
                return list;
            });

        public Task<(string Result, int? Id)> AddHolidayAsync(int customerId, DateTime date, string name, string? by) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_Attendance_Holidays_Add", c);
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
                cmd.Parameters.Add("@HolidayDate", SqlDbType.Date).Value = date.Date;
                cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = name;
                cmd.Parameters.Add("@By", SqlDbType.NVarChar, 256).Value = (object?)by ?? DBNull.Value;
                try
                {
                    await using var r = await cmd.ExecuteReaderAsync();
                    if (!await r.ReadAsync()) return ("error", (int?)null);
                    return (Str(r, "Result") ?? "error", Int(r, "Id"));
                }
                catch (SqlException ex) when (ex.Number is 2627 or 2601) // the same date added at the same moment
                {
                    return ("exists", (int?)null);
                }
            });

        public Task<int> DeleteHolidayAsync(int customerId, int id) =>
            Run(async c =>
            {
                await using var cmd = SP("dbo.WN_Attendance_Holidays_Delete", c);
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                cmd.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;
                return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
            });

        // ---- helpers ----

        private static async Task<AttendanceCustomerRow?> ReadCustomerAsync(SqlCommand cmd)
        {
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            return new AttendanceCustomerRow
            {
                CustomerId = Int(r, "CustomerId") ?? 0,
                CustomerName = Str(r, "CustomerName"),
                Enabled = Bool(r, "AttendanceEnabled")
            };
        }

        private async Task<T> Run<T>(Func<SqlConnection, Task<T>> body)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                return await body(c);
            }
            catch (SqlException ex) when (ex.Number is 208 or 2812 or 207) // invalid object / procedure / column: ATT_01 not run
            {
                throw new AttendanceNotInstalledException(ex);
            }
        }

        private static SqlCommand SP(string name, SqlConnection c) => new(name, c) { CommandType = CommandType.StoredProcedure };

        private static int? Int(SqlDataReader r, string col) => r[col] is DBNull ? null : Convert.ToInt32(r[col]);
        private static string? Str(SqlDataReader r, string col) => r[col] is DBNull ? null : Convert.ToString(r[col]);
        private static DateTime? Date(SqlDataReader r, string col) => r[col] is DBNull ? null : Convert.ToDateTime(r[col]);
        private static bool Bool(SqlDataReader r, string col) => r[col] is not DBNull && Convert.ToBoolean(r[col]);
    }
}
