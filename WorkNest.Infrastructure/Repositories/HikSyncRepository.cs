using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Data access for the Hikvision sync jobs. Stored procedures only (no SQL text here):
    /// the HIK procedures that already exist (WN_HIK_Device_SetOnline, WN_HIK_Log_Write, WN_HIK_Settings_*,
    /// WN_HIK_Expiry_Run, WN_HIK_Grant_SetState, WN_HIK_Grant_PendingEmployees) plus the WN_HIK_Sync_* procedures
    /// created by Database/HikSync/WN_HIK_Sync_Procedures.txt.
    /// </summary>
    public class HikSyncRepository : IHikSyncRepository
    {
        private readonly string _connectionString;

        public HikSyncRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        // ---- plumbing: stored procedures only -------------------------------------------------

        private async Task<List<IDictionary<string, object?>>> SpAsync(string procedure, params (string Name, object? Value)[] ps)
        {
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            await using var cmd = new SqlCommand("dbo." + procedure, c) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
            foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

            var rows = new List<IDictionary<string, object?>>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                rows.Add(row);
            }
            return rows;
        }

        private static string? Str(IDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v != null ? v.ToString() : null;
        private static int Int(IDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v != null ? Convert.ToInt32(v) : 0;
        private static DateTime? Date(IDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v is DateTime d ? d : null;

        private static HikSyncDevice MapDevice(IDictionary<string, object?> r) => new()
        {
            Id = Int(r, "Id"),
            Name = Str(r, "Device_Name") ?? $"Device {Int(r, "Id")}",
            Host = Str(r, "Host") ?? string.Empty,
            Host2 = Str(r, "Host2"),
            Port = Int(r, "Port") > 0 ? Int(r, "Port") : 80,
            UseHttps = r.TryGetValue("Use_https", out var https) && https != null && Convert.ToBoolean(https),
            Username = Str(r, "Username") ?? string.Empty,
            Password = Str(r, "Password") ?? string.Empty,
            Online = r.TryGetValue("Online", out var on) && on != null && Convert.ToBoolean(on),
            Code = Str(r, "Code"),
            Group = Str(r, "GroupName")
        };

        private static HikDevCacheRow MapSnap(IDictionary<string, object?> r) => new()
        {
            DeviceId = Int(r, "Device_id"),
            Users = Str(r, "Users_snapshot"),
            UsersAt = Date(r, "Users_UpdatedOn"),
            Cards = Str(r, "Cards_snapshot"),
            CardsAt = Date(r, "Cards_UpdatedOn")
        };

        // ---- devices, log, settings ------------------------------------------------------------

        public async Task<List<HikSyncDevice>> GetAllDevicesAsync() =>
            (await SpAsync("WN_HIK_Sync_Devices_Get")).Select(MapDevice).ToList();

        public async Task<HikSyncDevice?> GetDeviceAsync(int id) =>
            (await SpAsync("WN_HIK_Sync_Devices_Get", ("@DeviceId", id))).Select(MapDevice).FirstOrDefault();

        public Task SetDeviceOnlineAsync(int deviceId, bool online) =>
            SpAsync("WN_HIK_Device_SetOnline", ("@device_id", deviceId), ("@online", online ? 1 : 0));

        public Task LogAsync(int? employeeId, int? deviceId, string action, bool ok, string? detail) =>
            SpAsync("WN_HIK_Log_Write", ("@employee_id", employeeId), ("@device_id", deviceId), ("@action", action), ("@ok", ok ? 1 : 0), ("@detail", detail));

        public async Task<string?> GetSettingAsync(string key)
        {
            var rows = await SpAsync("WN_HIK_Settings_Get", ("@key", key));
            return rows.Count > 0 ? Str(rows[0], "value") : null;
        }

        public Task SetSettingAsync(string key, string value) =>
            SpAsync("WN_HIK_Settings_Set", ("@key", key), ("@value", value));

        // ---- employees / grants ----------------------------------------------------------------

        public Task<List<IDictionary<string, object?>>> RunExpiryAsync(string nowLocalIso) =>
            SpAsync("WN_HIK_Expiry_Run", ("@now", nowLocalIso));

        public async Task<IDictionary<string, object?>?> GetEmployeeAsync(int employeeId) =>
            (await SpAsync("WN_HIK_Sync_Employee_Get", ("@EmployeeId", employeeId))).FirstOrDefault();

        public Task<List<IDictionary<string, object?>>> GetGrantsForEmployeeAsync(int employeeId) =>
            SpAsync("WN_HIK_Sync_Grants_GetByEmployee", ("@EmployeeId", employeeId));

        public Task DeleteGrantsForEmployeeAsync(int employeeId) =>
            SpAsync("WN_HIK_Sync_Grants_DeleteByEmployee", ("@EmployeeId", employeeId));

        public Task DeleteGrantAsync(int grantId) =>
            SpAsync("WN_HIK_Sync_Grant_Delete", ("@GrantId", grantId));

        public Task SetGrantStateAsync(int grantId, string state, string? error) =>
            SpAsync("WN_HIK_Grant_SetState", ("@grant_id", grantId), ("@state", state), ("@error", error));

        public async Task<List<int>> GetPendingGrantEmployeeIdsAsync() =>
            (await SpAsync("WN_HIK_Grant_PendingEmployees")).Select(r => Int(r, "employee_id")).Where(id => id > 0).ToList();

        public async Task<int> CountPendingGrantsAsync()
        {
            var rows = await SpAsync("WN_HIK_Sync_Grants_CountPending");
            return rows.Count > 0 ? Int(rows[0], "n") : 0;
        }

        // ---- machine snapshots -----------------------------------------------------------------

        public async Task<List<HikDevCacheRow>> GetSnapshotsAsync() =>
            (await SpAsync("WN_HIK_Sync_DevCache_Get")).Select(MapSnap).ToList();

        public async Task<HikDevCacheRow?> GetSnapshotAsync(int deviceId) =>
            (await SpAsync("WN_HIK_Sync_DevCache_Get", ("@DeviceId", deviceId))).Select(MapSnap).FirstOrDefault();

        public Task SaveSnapshotAsync(int deviceId, bool cards, string json) =>
            SpAsync("WN_HIK_Sync_DevCache_Save", ("@DeviceId", deviceId), ("@IsCards", cards), ("@Json", json));

        public Task InvalidateSnapshotsAsync(int? deviceId) =>
            SpAsync("WN_HIK_Sync_DevCache_Invalidate", ("@DeviceId", deviceId));

        // ---- events ----------------------------------------------------------------------------

        public async Task<(long LastSerial, string? LastTime)> GetLastEventAsync(int deviceId)
        {
            var r = (await SpAsync("WN_HIK_Sync_Events_GetLast", ("@DeviceId", deviceId))).FirstOrDefault();
            var serial = r != null && r.TryGetValue("LastSerial", out var m) && m != null ? Convert.ToInt64(m) : 0L;
            return (serial, r != null ? Str(r, "LastTime") : null);
        }

        public async Task<bool> TryInsertEventAsync(int deviceId, string deviceName, string? employeeNo, string? name, string? cardNo,
            int? accessEvent, string accessEventDetails, long? machineEventNo, DateTime eventTime)
        {
            var r = (await SpAsync("WN_HIK_Sync_Event_Insert",
                ("@DeviceId", deviceId), ("@DeviceName", deviceName), ("@EmployeeNo", employeeNo), ("@Name", name), ("@CardNo", cardNo),
                ("@AccessEvent", accessEvent), ("@AccessEventDetails", accessEventDetails), ("@MachineEventNo", machineEventNo), ("@EventTime", eventTime)))
                .FirstOrDefault();
            return r != null && Int(r, "Inserted") == 1; // 0 = duplicate serial, already archived
        }

        // ---- queued operations -----------------------------------------------------------------

        public Task<List<IDictionary<string, object?>>> GetPendingOpsAsync(int top) =>
            SpAsync("WN_HIK_Sync_PendingOps_Get", ("@Top", top));

        public Task DeletePendingOpAsync(int id) =>
            SpAsync("WN_HIK_Sync_PendingOp_Delete", ("@Id", id));

        public Task FailPendingOpAsync(int id, string error) =>
            SpAsync("WN_HIK_Sync_PendingOp_Fail", ("@Id", id), ("@Error", error.Length > 400 ? error[..400] : error));

        // ---- vaults ----------------------------------------------------------------------------

        public async Task<List<HikFingerprintTemplate>> GetFpTemplatesAsync(string employeeNo, string name) =>
            (await SpAsync("WN_HIK_Sync_FpVault_Get", ("@EmployeeNo", employeeNo), ("@Name", (name ?? string.Empty).Trim())))
            .Select(r => new HikFingerprintTemplate { FingerPrintId = Int(r, "finger_no") > 0 ? Int(r, "finger_no") : 1, FingerData = Str(r, "template") ?? string.Empty })
            .Where(t => t.FingerData.Length > 0).ToList();

        public Task SaveFpTemplateAsync(string employeeNo, string name, int fingerNo, string template) =>
            SpAsync("WN_HIK_Sync_FpVault_Save", ("@EmployeeNo", employeeNo), ("@Name", (name ?? string.Empty).Trim()),
                ("@FingerNo", fingerNo > 0 ? fingerNo : 1), ("@Template", template));

        public async Task<string?> GetFaceTemplateAsync(string employeeNo, string name)
        {
            var rows = await SpAsync("WN_HIK_Sync_FaceVault_Get", ("@EmployeeNo", employeeNo), ("@Name", (name ?? string.Empty).Trim()));
            return rows.Count > 0 ? Str(rows[0], "model_data") : null;
        }

        public Task SaveFaceTemplateAsync(string employeeNo, string name, string modelData) =>
            SpAsync("WN_HIK_Sync_FaceVault_Save", ("@EmployeeNo", employeeNo), ("@Name", (name ?? string.Empty).Trim()), ("@ModelData", modelData));

        public async Task<HashSet<string>> GetFaceVaultKeysAsync() =>
            (await SpAsync("WN_HIK_Sync_FaceVault_Keys"))
            .Select(r => $"{Str(r, "employee_no")}||{(Str(r, "name") ?? string.Empty).Trim().ToLowerInvariant()}")
            .ToHashSet();

        // ---- WN_HIK_Users rebuild --------------------------------------------------------------

        public async Task<List<HikUserMeta>> GetUserMetaAsync() =>
            (await SpAsync("WN_HIK_Sync_UserMeta_Get"))
            .Select(r => new HikUserMeta
            {
                EmployeeNo = Str(r, "employee_no") ?? string.Empty,
                Name = (Str(r, "name") ?? string.Empty).Trim(),
                Cnic = Str(r, "cnic"),
                TagId = r.TryGetValue("tag_id", out var t) && t != null ? Convert.ToInt32(t) : null
            }).ToList();

        /// <summary>The whole table is replaced in one procedure call (one transaction) from a JSON array.</summary>
        public Task RebuildUsersAsync(IReadOnlyList<HikUserRow> rows)
        {
            var json = JsonSerializer.Serialize(rows.Select(u => new
            {
                employee_no = u.EmployeeNo, name = u.Name, room = u.Room, role = u.Role,
                machines = u.Machines, machine_count = u.MachineCount, cnic = u.Cnic, tag_id = u.TagId
            }));
            return SpAsync("WN_HIK_Sync_Users_Rebuild", ("@UsersJson", json));
        }

        // ---- card grant mirroring --------------------------------------------------------------

        public async Task<List<(int Id, string CardNo)>> GetActiveCardEmployeesAsync() =>
            (await SpAsync("WN_HIK_Sync_CardEmployees_Get")).Select(r => (Int(r, "id"), Str(r, "card_no") ?? string.Empty)).ToList();

        public Task UpdateCardHolderAsync(int id, string? employeeName, string? employeeNo) =>
            SpAsync("WN_HIK_Sync_Card_SetHolder", ("@Id", id), ("@EmployeeName", employeeName), ("@EmployeeNo", employeeNo));

        public Task InsertSyncedGrantAsync(int employeeId, int deviceId) =>
            SpAsync("WN_HIK_Sync_Grant_InsertSynced", ("@EmployeeId", employeeId), ("@DeviceId", deviceId));

        // ---- booking renewals ------------------------------------------------------------------

        public async Task<List<string>> GetBookingRefsAsync() =>
            (await SpAsync("WN_HIK_Sync_BookingRefs_Get")).Select(r => Str(r, "booking_ref"))
            .Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();

        public async Task<IDictionary<string, object?>?> GetBookingAsync(int bookingId) =>
            (await SpAsync("WN_HIK_Sync_Booking_Get", ("@BookingId", bookingId))).FirstOrDefault();

        public async Task<List<(int Id, string? ValidEnd)>> GetAttendeesByRefAsync(string bookingRef) =>
            (await SpAsync("WN_HIK_Sync_Attendees_GetByRef", ("@BookingRef", bookingRef))).Select(r => (Int(r, "id"), Str(r, "ve"))).ToList();

        public async Task<IDictionary<string, object?>?> GetNextBookingAsync(int oldId, string? customerCode, int spaceId, DateTime oldEnd) =>
            (await SpAsync("WN_HIK_Sync_Booking_GetNext", ("@OldId", oldId), ("@CustomerCode", customerCode), ("@SpaceId", spaceId), ("@OldEnd", oldEnd)))
            .FirstOrDefault();

        public async Task<bool> RefHasAttendeesAsync(string bookingRef) =>
            (await SpAsync("WN_HIK_Sync_Ref_HasAttendees", ("@BookingRef", bookingRef))).Count > 0;

        public Task RetargetVisitorsAsync(string fromRef, string toRef, string newEndLocalIso) =>
            SpAsync("WN_HIK_Sync_Visitors_Retarget", ("@FromRef", fromRef), ("@ToRef", toRef), ("@NewEnd", newEndLocalIso));

        public Task ResetGrantsToPendingAsync(int employeeId) =>
            SpAsync("WN_HIK_Sync_Grants_ResetToPending", ("@EmployeeId", employeeId));
    }
}
