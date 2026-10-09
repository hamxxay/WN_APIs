using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    /// <summary>
    /// Hikvision machine ⇄ database sync (replaces the HIK_Access-Control Node scheduler, which can no longer
    /// reach SAC400). Each job reads the machines over ISAPI and writes the WN_HIK_* tables.
    /// </summary>
    public interface IHikSyncService
    {
        /// <summary>Job names accepted by <see cref="RunJobAsync"/>.</summary>
        IReadOnlyList<string> JobNames { get; }

        /// <summary>Runs one job now (skipped when that job is already running). Returns its status.</summary>
        Task<HikSyncJobStatus> RunJobAsync(string job);

        /// <summary>Last run of every job.</summary>
        IReadOnlyList<HikSyncJobStatus> GetStatus();
    }
}
