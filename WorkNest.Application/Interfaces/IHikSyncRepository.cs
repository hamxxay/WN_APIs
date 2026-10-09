using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// SQL used by the Hikvision sync jobs (port of HIK_Access-Control scheduler.js / sync.js / machineCache.js).
    /// Reads and writes only WN_HIK_* objects, plus read-only WN_Bookings for booking renewals.
    /// Rows are case-insensitive dictionaries, as the HIK procedures return them.
    /// </summary>
    public interface IHikSyncRepository
    {
        // Devices, log, settings
        Task<List<HikSyncDevice>> GetAllDevicesAsync();
        Task<HikSyncDevice?> GetDeviceAsync(int id);
        Task SetDeviceOnlineAsync(int deviceId, bool online);
        Task LogAsync(int? employeeId, int? deviceId, string action, bool ok, string? detail);
        Task<string?> GetSettingAsync(string key);
        Task SetSettingAsync(string key, string value);

        // Employees / grants
        Task<List<IDictionary<string, object?>>> RunExpiryAsync(string nowLocalIso);
        Task<IDictionary<string, object?>?> GetEmployeeAsync(int employeeId);
        Task<List<IDictionary<string, object?>>> GetGrantsForEmployeeAsync(int employeeId);
        Task DeleteGrantsForEmployeeAsync(int employeeId);
        Task DeleteGrantAsync(int grantId);
        Task SetGrantStateAsync(int grantId, string state, string? error);
        Task<List<int>> GetPendingGrantEmployeeIdsAsync();
        Task<int> CountPendingGrantsAsync();

        // Machine snapshots (WN_HIK_DevCache)
        Task<List<HikDevCacheRow>> GetSnapshotsAsync();
        Task<HikDevCacheRow?> GetSnapshotAsync(int deviceId);
        Task SaveSnapshotAsync(int deviceId, bool cards, string json);
        Task InvalidateSnapshotsAsync(int? deviceId);

        // Events
        Task<(long LastSerial, string? LastTime)> GetLastEventAsync(int deviceId);
        /// <summary>false when the event is already archived (duplicate serial).</summary>
        Task<bool> TryInsertEventAsync(int deviceId, string deviceName, string? employeeNo, string? name, string? cardNo,
            int? accessEvent, string accessEventDetails, long? machineEventNo, DateTime eventTime);

        // Queued operations
        Task<List<IDictionary<string, object?>>> GetPendingOpsAsync(int top);
        Task DeletePendingOpAsync(int id);
        Task FailPendingOpAsync(int id, string error);

        // Credential vaults
        Task<List<HikFingerprintTemplate>> GetFpTemplatesAsync(string employeeNo, string name);
        Task SaveFpTemplateAsync(string employeeNo, string name, int fingerNo, string template);
        Task<string?> GetFaceTemplateAsync(string employeeNo, string name);
        Task SaveFaceTemplateAsync(string employeeNo, string name, string modelData);
        Task<HashSet<string>> GetFaceVaultKeysAsync();

        // WN_HIK_Users rebuild
        Task<List<HikUserMeta>> GetUserMetaAsync();
        Task RebuildUsersAsync(IReadOnlyList<HikUserRow> rows);

        // Card grant mirroring
        Task<List<(int Id, string CardNo)>> GetActiveCardEmployeesAsync();
        Task UpdateCardHolderAsync(int id, string? employeeName, string? employeeNo);
        Task InsertSyncedGrantAsync(int employeeId, int deviceId);

        // Booking renewals (WNB- attendees)
        Task<List<string>> GetBookingRefsAsync();
        Task<IDictionary<string, object?>?> GetBookingAsync(int bookingId);
        Task<List<(int Id, string? ValidEnd)>> GetAttendeesByRefAsync(string bookingRef);
        Task<IDictionary<string, object?>?> GetNextBookingAsync(int oldId, string? customerCode, int spaceId, DateTime oldEnd);
        Task<bool> RefHasAttendeesAsync(string bookingRef);
        Task RetargetVisitorsAsync(string fromRef, string toRef, string newEndLocalIso);
        Task ResetGrantsToPendingAsync(int employeeId);
    }

    public class HikDevCacheRow
    {
        public int DeviceId { get; set; }
        public string? Users { get; set; }
        public DateTime? UsersAt { get; set; }
        public string? Cards { get; set; }
        public DateTime? CardsAt { get; set; }
    }

    public class HikUserMeta
    {
        public string EmployeeNo { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Cnic { get; set; }
        public int? TagId { get; set; }
    }

    public class HikUserRow
    {
        public string EmployeeNo { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Room { get; set; }
        public string? Role { get; set; }
        public string Machines { get; set; } = "[]";
        public int MachineCount { get; set; }
        public string? Cnic { get; set; }
        public int? TagId { get; set; }
    }
}
