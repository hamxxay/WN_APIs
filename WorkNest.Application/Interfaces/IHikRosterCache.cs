using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// The machine roster / card-table cache shared by the sync jobs and the machine endpoints
    /// (port of HIK machineCache.js): memory, then the WN_HIK_DevCache snapshot, then a live scan.
    /// Returned lists are shared: read them, do not change them.
    /// </summary>
    public interface IHikRosterCache
    {
        /// <summary>Every person on the machine (raw ISAPI UserInfo). Throws when the machine can't be read
        /// and no snapshot exists. Default max age: 2 minutes.</summary>
        Task<IReadOnlyList<JsonObject>> GetRosterAsync(HikSyncDevice device, TimeSpan? maxAge = null);

        /// <summary>Every card on the machine with its holder. Throws when unreadable. Default max age: 5 minutes.</summary>
        Task<IReadOnlyList<(string CardNo, string EmployeeNo)>> GetCardTableAsync(HikSyncDevice device, TimeSpan? maxAge = null);

        /// <summary>Forget one machine's (or every machine's, when null) roster and cards so the next read is live.</summary>
        Task InvalidateAsync(int? deviceId = null);
    }
}
